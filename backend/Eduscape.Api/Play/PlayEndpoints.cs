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

/// <summary>
/// The team's progress on an unsolved lock: the step it is on, the step's hint once the team has earned it with
/// <see cref="Team.WrongAttemptsBeforeHint"/> wrong answers, and how many seconds it must still wait after a wrong answer.
/// </summary>
public sealed record LockProgressDto(Guid LockId, int SolvedSteps, string? Hint, int CooldownSeconds);

/// <summary><c>OpenedDoorIds</c> are the open doors and exit doors, which follow from the solved locks.</summary>
public sealed record TeamStateDto(
    Guid Id,
    TeamColor Color,
    Guid[] SolvedLockIds,
    Guid[] OpenedDoorIds,
    LockProgressDto[] Locks,
    DateTimeOffset? EscapedAt)
{
    public static TeamStateDto From(Team team, EscapeRoom room, DateTimeOffset now) => new(
        team.Id,
        team.Color,
        [.. team.SolvedLockIds],
        [.. RoomProgress.For(room, team).OpenDoorwayIds],
        [.. room.Objects.OfType<Lock>()
            .Where(l => !team.IsLockSolved(l.Id))
            .Select(l => new LockProgressDto(
                l.Id,
                team.SolvedSteps(l.Id),
                CurrentHint(team, l),
                team.CooldownRemainingSeconds(l.Id, room.WrongAnswerCooldownSeconds, now)))
            .Where(p => p.SolvedSteps > 0 || p.Hint is not null || p.CooldownSeconds > 0)],
        team.EscapedAt);

    /// <summary>The hint of the step the team is on, once earned; null otherwise.</summary>
    public static string? CurrentHint(Team team, Lock @lock) =>
        team.IsHintVisible(@lock.Id) && team.SolvedSteps(@lock.Id) is var step && step < @lock.Steps.Count
            ? @lock.Steps[step].Hint
            : null;
}

/// <summary>
/// The whole state a student's device needs, sent on join and on every reconnect. <c>IsPreview</c> is set when a
/// teacher is trying their own room. <c>CurrentSceneId</c> is the scene the student was last in, so a reload (or a
/// preview from a scene) starts there.
/// </summary>
public sealed record PlayStateDto(
    Guid SessionId,
    SessionMode Mode,
    SessionStatus Status,
    Guid ParticipantId,
    string? Nickname,
    TeamStateDto Team,
    PlayRoomDto Room,
    bool IsPreview,
    Guid? CurrentSceneId);

/// <summary>
/// An answer to the lock's step <c>StepIndex</c>: <c>Answer</c> for typed and multiple-choice answers, <c>Order</c>
/// (the item ids in the student's order) for order steps.
/// </summary>
public sealed record AnswerRequest(string? DeviceToken, Guid LockId, int StepIndex, string? Answer, Guid[]? Order = null);

/// <summary>
/// The correct answer is never sent. <c>SolvedSteps</c> is the team's progress on the lock, <c>Hint</c> the current
/// step's hint once earned, <c>CooldownSeconds</c> how long the team must wait before answering again, and
/// <c>OpenedDoorIds</c> the doors that opened because of this answer.
/// </summary>
public sealed record AnswerResponse(
    AnswerOutcome Outcome,
    int SolvedSteps,
    bool LockSolved,
    string? Hint,
    int CooldownSeconds,
    Guid[] OpenedDoorIds);

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

        // Answers are checked only here, on the server. Doors open by themselves when an answer meets their condition;
        // students walk through an open exit door to escape.
        play.MapPost("/answer", AnswerAsync);
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

        var state = await LoadStateAsync(session, participant, db, imageUrls, timeProvider.GetUtcNow(), cancellationToken);
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

        return TypedResults.Ok(await LoadStateAsync(session, participant, db, imageUrls, timeProvider.GetUtcNow(), cancellationToken));
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
        var submission = new AnswerSubmission(request.Answer, request.Order);
        var done = await actions.RunAsync(
            request.DeviceToken,
            (room, team, now) => Gameplay.SubmitAnswer(room, team, request.LockId, request.StepIndex, submission, now),
            cancellationToken);
        if (done is null)
        {
            return DeviceNotRecognized();
        }

        var result = done.Result;
        var @lock = done.Room.Objects.OfType<Lock>().Single(l => l.Id == request.LockId);
        var hint = TeamStateDto.CurrentHint(done.Team, @lock);
        var teamId = done.Team.Id;

        switch (result.Outcome)
        {
            case AnswerOutcome.Correct when result.LockSolved:
                await notifier.LockSolvedAsync(teamId, request.LockId);
                foreach (var doorId in result.OpenedDoorwayIds)
                {
                    await notifier.DoorOpenedAsync(teamId, doorId);
                }

                break;
            case AnswerOutcome.Correct:
                await notifier.StepSolvedAsync(teamId, request.LockId, result.SolvedSteps);
                break;
            case AnswerOutcome.Wrong:
                if (result.HintRevealedNow && hint is not null)
                {
                    await notifier.HintRevealedAsync(teamId, request.LockId, hint);
                }

                if (result.CooldownSeconds > 0)
                {
                    await notifier.LockCooldownAsync(teamId, request.LockId, result.CooldownSeconds);
                }

                break;
        }

        // A wrong answer changes what the dashboard shows too: the attempts on the lock the team is stuck on.
        if (result.Outcome is AnswerOutcome.Correct or AnswerOutcome.Wrong)
        {
            await dashboard.SessionChangedAsync(done.Team.SessionId);
        }

        return TypedResults.Ok(new AnswerResponse(
            result.Outcome, result.SolvedSteps, result.LockSolved, hint, result.CooldownSeconds, [.. result.OpenedDoorwayIds]));
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
        DateTimeOffset now,
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
            TeamStateDto.From(team, room, now),
            PlayRoomDto.From(room, imageUrls, team.Id),
            session.IsPreview,
            participant.CurrentSceneId);
    }

    private static IResult PinNotFound() =>
        TypedResults.Json(new ApiError(ApiErrors.PinNotFound), statusCode: StatusCodes.Status404NotFound);

    private static IResult DeviceNotRecognized() =>
        TypedResults.Json(new ApiError(ApiErrors.DeviceNotRecognized), statusCode: StatusCodes.Status404NotFound);
}
