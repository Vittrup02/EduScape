using Eduscape.Api.Errors;
using Eduscape.Api.Hosting;
using Eduscape.Api.Sessions;
using Eduscape.Api.Uploads;
using Eduscape.Domain.Common;
using Eduscape.Domain.Rooms;
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

/// <summary>A lock's hint, which the team has earned with <see cref="Team.WrongAttemptsBeforeHint"/> wrong answers.</summary>
public sealed record RevealedHintDto(Guid LockId, string Hint);

public sealed record TeamStateDto(
    Guid Id,
    TeamColor Color,
    Guid[] SolvedLockIds,
    Guid[] OpenedDoorIds,
    RevealedHintDto[] RevealedHints,
    DateTimeOffset? EscapedAt)
{
    public static TeamStateDto From(Team team, EscapeRoom room) => new(
        team.Id,
        team.Color,
        [.. team.SolvedLockIds],
        [.. team.OpenedDoorIds],
        [.. room.Objects.OfType<Lock>()
            .Where(l => l.Hint is not null && team.IsHintVisible(l.Id))
            .Select(l => new RevealedHintDto(l.Id, l.Hint!))],
        team.EscapedAt);
}

/// <summary>
/// The whole state a student's device needs, sent on join and on every reconnect. <c>IsPreview</c> is set when a
/// teacher is trying their own room.
/// </summary>
public sealed record PlayStateDto(
    Guid SessionId,
    SessionMode Mode,
    SessionStatus Status,
    Guid ParticipantId,
    string? Nickname,
    TeamStateDto Team,
    PlayRoomDto Room,
    bool IsPreview);

public sealed record AnswerRequest(string? DeviceToken, Guid LockId, string? Answer);

/// <summary><c>Hint</c> is set once the team has earned the lock's hint. The correct answer is never sent.</summary>
public sealed record AnswerResponse(AnswerOutcome Outcome, string? Hint);

public sealed record OpenDoorRequest(string? DeviceToken, Guid DoorId);

public sealed record OpenExitDoorRequest(string? DeviceToken, Guid ExitDoorId);

public sealed record EnterSceneRequest(string? DeviceToken, Guid SceneId);

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

        // Answers are checked only here, on the server.
        play.MapPost("/answer", AnswerAsync);
        play.MapPost("/doors/open", OpenDoorAsync);
        play.MapPost("/exit", OpenExitDoorAsync);

        // Which scene a student looks at is their own choice; it is reported only for the teacher's dashboard.
        play.MapPost("/scene", EnterSceneAsync);
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
        JoinRequest request,
        EduscapeDbContext db,
        TimeProvider timeProvider,
        ImageUrls imageUrls,
        IDashboardNotifier dashboard,
        CancellationToken cancellationToken)
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
        await dashboard.SessionChangedAsync(session.Id);

        var state = await LoadStateAsync(session, participant, db, imageUrls, cancellationToken);
        return TypedResults.Ok(new JoinResponse(deviceToken, state));
    }

    /// <summary>
    /// Sends a returning device back to its team with the team's whole state in one call. Works while the session is
    /// locked (students already in keep playing), but not once it is closed or expired.
    /// </summary>
    private static async Task<IResult> ReconnectAsync(
        ReconnectRequest request,
        EduscapeDbContext db,
        TimeProvider timeProvider,
        ImageUrls imageUrls,
        CancellationToken cancellationToken)
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

        return TypedResults.Ok(await LoadStateAsync(session, participant, db, imageUrls, cancellationToken));
    }

    // The team and the teacher's dashboard are notified only of changes, and only after they are saved. A lock solved
    // by two students at once is one LockSolved event. Notifications ignore the request's cancellation: once saved,
    // the team must hear of it.

    private static async Task<IResult> AnswerAsync(
        AnswerRequest request,
        TeamActions actions,
        ITeamNotifier notifier,
        IDashboardNotifier dashboard,
        CancellationToken cancellationToken)
    {
        var done = await actions.RunAsync(
            request.DeviceToken,
            (room, team, now) => Gameplay.SubmitAnswer(room, team, request.LockId, request.Answer, now),
            cancellationToken);
        if (done is null)
        {
            return DeviceNotRecognized();
        }

        var hint = done.Team.IsHintVisible(request.LockId)
            ? done.Room.Objects.OfType<Lock>().Single(l => l.Id == request.LockId).Hint
            : null;

        if (done.Result.Outcome == AnswerOutcome.Correct)
        {
            await notifier.LockSolvedAsync(done.Team.Id, request.LockId);
        }
        else if (done.Result.HintRevealedNow && hint is not null)
        {
            await notifier.HintRevealedAsync(done.Team.Id, request.LockId, hint);
        }

        // A wrong answer changes what the dashboard shows too: the attempts on the lock the team is stuck on.
        if (done.Result.Outcome != AnswerOutcome.AlreadySolved)
        {
            await dashboard.SessionChangedAsync(done.Team.SessionId);
        }

        return TypedResults.Ok(new AnswerResponse(done.Result.Outcome, hint));
    }

    private static async Task<IResult> OpenDoorAsync(
        OpenDoorRequest request,
        TeamActions actions,
        ITeamNotifier notifier,
        IDashboardNotifier dashboard,
        CancellationToken cancellationToken)
    {
        var done = await actions.RunAsync(
            request.DeviceToken,
            (room, team, now) => Gameplay.OpenDoor(room, team, request.DoorId, now),
            cancellationToken);
        if (done is null)
        {
            return DeviceNotRecognized();
        }

        if (done.Result)
        {
            await notifier.DoorOpenedAsync(done.Team.Id, request.DoorId);
            await dashboard.SessionChangedAsync(done.Team.SessionId);
        }

        return TypedResults.NoContent();
    }

    private static async Task<IResult> OpenExitDoorAsync(
        OpenExitDoorRequest request,
        TeamActions actions,
        ITeamNotifier notifier,
        IDashboardNotifier dashboard,
        CancellationToken cancellationToken)
    {
        var done = await actions.RunAsync(
            request.DeviceToken,
            (room, team, now) => Gameplay.OpenExitDoor(room, team, request.ExitDoorId, now),
            cancellationToken);
        if (done is null)
        {
            return DeviceNotRecognized();
        }

        if (done.Result)
        {
            await notifier.TeamEscapedAsync(done.Team.Id);
            await dashboard.SessionChangedAsync(done.Team.SessionId);
        }

        return TypedResults.NoContent();
    }

    private static async Task<IResult> EnterSceneAsync(
        EnterSceneRequest request, TeamActions actions, IDashboardNotifier dashboard, CancellationToken cancellationToken)
    {
        var done = await actions.RunAsync(
            request.DeviceToken,
            (room, team, participant, now) => Gameplay.EnterScene(room, team, participant, request.SceneId, now),
            cancellationToken);
        if (done is null)
        {
            return DeviceNotRecognized();
        }

        if (done.Result)
        {
            await dashboard.SessionChangedAsync(done.Team.SessionId);
        }

        return TypedResults.NoContent();
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
        Session session,
        Participant participant,
        EduscapeDbContext db,
        ImageUrls imageUrls,
        CancellationToken cancellationToken)
    {
        var team = await TeamActions.LoadTeamAsync(db, participant.TeamId, cancellationToken);
        var room = await TeamActions.LoadRoomAsync(db, session.EscapeRoomId, cancellationToken);

        return new PlayStateDto(
            session.Id,
            session.Mode,
            session.Status,
            participant.Id,
            participant.Nickname,
            TeamStateDto.From(team, room),
            PlayRoomDto.From(room, imageUrls),
            session.IsPreview);
    }

    private static IResult PinNotFound() =>
        TypedResults.Json(new ApiError(ApiErrors.PinNotFound), statusCode: StatusCodes.Status404NotFound);

    private static IResult DeviceNotRecognized() =>
        TypedResults.Json(new ApiError(ApiErrors.DeviceNotRecognized), statusCode: StatusCodes.Status404NotFound);
}
