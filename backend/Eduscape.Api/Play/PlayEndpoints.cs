using Eduscape.Api.Errors;
using Eduscape.Api.Hosting;
using Eduscape.Domain.Common;
using Eduscape.Domain.Sessions;
using Eduscape.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Eduscape.Api.Play;

public sealed record LookupRequest(string? Pin);

public sealed record PlayTeamDto(Guid Id, TeamColor Color);

/// <summary>What a student sees after entering the PIN. <c>Teams</c> is empty in individual mode.</summary>
public sealed record LookupResponse(SessionMode Mode, PlayTeamDto[] Teams);

/// <summary><c>TeamId</c> is ignored in individual mode, where the server creates the team.</summary>
public sealed record JoinRequest(string? Pin, Guid? TeamId, string? Nickname);

public sealed record ReconnectRequest(string? DeviceToken);

public sealed record TeamStateDto(
    Guid Id,
    TeamColor Color,
    Guid[] SolvedLockIds,
    Guid[] OpenedDoorIds,
    DateTimeOffset? EscapedAt);

/// <summary>The whole state a student's device needs, sent on join and on every reconnect.</summary>
public sealed record PlayStateDto(
    Guid SessionId,
    SessionMode Mode,
    SessionStatus Status,
    Guid ParticipantId,
    string? Nickname,
    TeamStateDto Team);

/// <summary>The device token is returned only here; the server keeps just its hash.</summary>
public sealed record JoinResponse(string DeviceToken, PlayStateDto State);

/// <summary>
/// Endpoints for students, who never log in: they join with the PIN and then identify themselves with the device
/// token the server issued. PINs and tokens are sent in the body, never in the URL, so they stay out of logs.
/// </summary>
public static class PlayEndpoints
{
    public static IEndpointRouteBuilder MapPlayEndpoints(this IEndpointRouteBuilder app)
    {
        var play = app.MapGroup("/api/play");
        play.MapPost("/lookup", LookupAsync).RequireRateLimiting(RateLimiting.JoinPolicy);
        play.MapPost("/join", JoinAsync).RequireRateLimiting(RateLimiting.JoinPolicy);

        // Not limited like the PIN endpoints: a 256-bit token cannot be guessed, and a whole class reconnects at once
        // when the backend wakes up.
        play.MapPost("/reconnect", ReconnectAsync);
        return app;
    }

    private static async Task<IResult> LookupAsync(
        LookupRequest request, EduscapeDbContext db, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var session = await FindJoinableSessionAsync(request.Pin, db, timeProvider, cancellationToken);
        if (session is null)
        {
            return PinNotFound();
        }

        var teams = session.Mode == SessionMode.Team
            ? session.Teams.OrderBy(t => t.Color).Select(t => new PlayTeamDto(t.Id, t.Color)).ToArray()
            : [];
        return TypedResults.Ok(new LookupResponse(session.Mode, teams));
    }

    private static async Task<IResult> JoinAsync(
        JoinRequest request, EduscapeDbContext db, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var session = await FindJoinableSessionAsync(request.Pin, db, timeProvider, cancellationToken);
        if (session is null)
        {
            return PinNotFound();
        }

        var deviceToken = DeviceToken.Create();
        var participant = session.Join(
            request.TeamId, request.Nickname, DeviceToken.Hash(deviceToken), timeProvider.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);

        var state = await LoadStateAsync(session, participant, db, cancellationToken);
        return TypedResults.Ok(new JoinResponse(deviceToken, state));
    }

    /// <summary>
    /// Sends a returning device back to its team with the team's whole state in one call. Works while the session is
    /// locked (students already in keep playing), but not once it is closed or expired.
    /// </summary>
    private static async Task<IResult> ReconnectAsync(
        ReconnectRequest request, EduscapeDbContext db, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.DeviceToken))
        {
            return DeviceNotRecognized();
        }

        var hash = DeviceToken.Hash(request.DeviceToken);
        var participant = await db.Participants.SingleOrDefaultAsync(p => p.DeviceTokenHash == hash, cancellationToken);
        if (participant is null)
        {
            return DeviceNotRecognized();
        }

        var session = await db.Sessions
            .SingleAsync(s => s.Teams.Any(t => t.Id == participant.TeamId), cancellationToken);
        if (session.CloseIfExpired(timeProvider.GetUtcNow()))
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        if (session.Status == SessionStatus.Closed)
        {
            throw new DomainException(DomainErrors.SessionClosed);
        }

        return TypedResults.Ok(await LoadStateAsync(session, participant, db, cancellationToken));
    }

    /// <summary>
    /// The not-closed session with this PIN, or null. Throws the reason it cannot be joined (expired or locked),
    /// so the student gets a helpful message.
    /// </summary>
    private static async Task<Session?> FindJoinableSessionAsync(
        string? pin, EduscapeDbContext db, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var trimmed = pin?.Trim();
        if (!Session.IsValidPin(trimmed))
        {
            throw new DomainException(DomainErrors.SessionPinInvalid);
        }

        // Closed sessions have released their PIN, which may already belong to a new session.
        var session = await db.Sessions
            .Include(s => s.Teams)
            .SingleOrDefaultAsync(s => s.Pin == trimmed && s.Status != SessionStatus.Closed, cancellationToken);
        session?.EnsureJoinable(timeProvider.GetUtcNow());
        return session;
    }

    private static async Task<PlayStateDto> LoadStateAsync(
        Session session, Participant participant, EduscapeDbContext db, CancellationToken cancellationToken)
    {
        var team = await db.Teams
            .Include(t => t.LockProgress)
            .Include(t => t.OpenedDoors)
            .AsSplitQuery()
            .SingleAsync(t => t.Id == participant.TeamId, cancellationToken);

        return new PlayStateDto(
            session.Id,
            session.Mode,
            session.Status,
            participant.Id,
            participant.Nickname,
            new TeamStateDto(
                team.Id,
                team.Color,
                [.. team.SolvedLockIds],
                [.. team.OpenedDoorIds],
                team.EscapedAt));
    }

    private static IResult PinNotFound() =>
        TypedResults.Json(new ApiError(ApiErrors.PinNotFound), statusCode: StatusCodes.Status404NotFound);

    private static IResult DeviceNotRecognized() =>
        TypedResults.Json(new ApiError(ApiErrors.DeviceNotRecognized), statusCode: StatusCodes.Status404NotFound);
}
