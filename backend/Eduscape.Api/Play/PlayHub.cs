using Eduscape.Api.Errors;
using Eduscape.Domain.Common;
using Eduscape.Domain.Sessions;
using Eduscape.Infrastructure.Persistence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Eduscape.Api.Play;

/// <summary>Events the server pushes to every device on a team. They carry ids only, never answers.</summary>
public interface IPlayClient
{
    /// <summary>The team answered the lock's last step: the lock is open.</summary>
    Task LockSolved(Guid lockId);

    /// <summary>The team answered a step of the lock; the next one (index <paramref name="solvedSteps"/>) is up.</summary>
    Task StepSolved(Guid lockId, int solvedSteps);

    /// <summary>The team has answered the lock's current step wrong <see cref="Team.WrongAttemptsBeforeHint"/> times.</summary>
    Task HintRevealed(Guid lockId, string hint);

    /// <summary>A wrong answer: the lock is locked for the team for this many seconds.</summary>
    Task LockCooldown(Guid lockId, int seconds);

    /// <summary>A door or exit door opened because the team met its condition; every device animates it.</summary>
    Task DoorOpened(Guid doorId);

    Task TeamEscaped();

    /// <summary>
    /// Sent to one student's devices only: the teacher moved the student to another team. The device subscribes
    /// again (a new connection, so it leaves the old team's group) and fetches the new team's state.
    /// </summary>
    Task TeamChanged();

    /// <summary>The teacher closed the session. The device forgets its token and stops showing the room.</summary>
    Task SessionClosed();
}

/// <summary>
/// Realtime channel for students. Each team is a SignalR group, and so is each student. Students only listen here;
/// actions go through the play endpoints, which push the resulting events to the groups.
/// </summary>
public sealed class PlayHub(EduscapeDbContext db, TimeProvider timeProvider) : Hub<IPlayClient>
{
    public const string Path = "/hubs/play";

    public static string TeamGroup(Guid teamId) => $"team:{teamId}";

    public static string ParticipantGroup(Guid participantId) => $"participant:{participantId}";

    /// <summary>
    /// Adds this connection to the device's team and to the student's own group. The token is sent as a message,
    /// not in the URL, so it stays out of logs. Returns null on success, or an error code:
    /// <see cref="ApiErrors.DeviceNotRecognized"/> or <see cref="DomainErrors.SessionClosed"/>. Clients call it again
    /// after every reconnect, because a new connection starts without groups, and then fetch the team's whole state.
    /// </summary>
    public async Task<string?> Subscribe(string? deviceToken)
    {
        if (string.IsNullOrEmpty(deviceToken))
        {
            return ApiErrors.DeviceNotRecognized;
        }

        var hash = DeviceToken.Hash(deviceToken);
        var participant = await db.Participants
            .Where(p => p.DeviceTokenHash == hash)
            .Select(p => new { p.Id, p.TeamId })
            .SingleOrDefaultAsync(Context.ConnectionAborted);
        if (participant is null)
        {
            return ApiErrors.DeviceNotRecognized;
        }

        var session = await db.Sessions.SingleAsync(
            s => s.Teams.Any(t => t.Id == participant.TeamId), Context.ConnectionAborted);
        if (!session.IsActive(timeProvider.GetUtcNow()))
        {
            return DomainErrors.SessionClosed;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, TeamGroup(participant.TeamId), Context.ConnectionAborted);
        await Groups.AddToGroupAsync(Context.ConnectionId, ParticipantGroup(participant.Id), Context.ConnectionAborted);
        return null;
    }
}

/// <summary>Pushes a team's progress to its devices. An interface so tests can count the events.</summary>
public interface ITeamNotifier
{
    Task LockSolvedAsync(Guid teamId, Guid lockId);

    Task StepSolvedAsync(Guid teamId, Guid lockId, int solvedSteps);

    Task HintRevealedAsync(Guid teamId, Guid lockId, string hint);

    Task LockCooldownAsync(Guid teamId, Guid lockId, int seconds);

    Task DoorOpenedAsync(Guid teamId, Guid doorId);

    Task TeamEscapedAsync(Guid teamId);

    Task TeamChangedAsync(Guid participantId);

    Task SessionClosedAsync(IReadOnlyCollection<Guid> teamIds);
}

internal sealed class SignalRTeamNotifier(IHubContext<PlayHub, IPlayClient> hub) : ITeamNotifier
{
    public Task LockSolvedAsync(Guid teamId, Guid lockId) => Team(teamId).LockSolved(lockId);

    public Task StepSolvedAsync(Guid teamId, Guid lockId, int solvedSteps) => Team(teamId).StepSolved(lockId, solvedSteps);

    public Task HintRevealedAsync(Guid teamId, Guid lockId, string hint) => Team(teamId).HintRevealed(lockId, hint);

    public Task LockCooldownAsync(Guid teamId, Guid lockId, int seconds) => Team(teamId).LockCooldown(lockId, seconds);

    public Task DoorOpenedAsync(Guid teamId, Guid doorId) => Team(teamId).DoorOpened(doorId);

    public Task TeamEscapedAsync(Guid teamId) => Team(teamId).TeamEscaped();

    public Task TeamChangedAsync(Guid participantId) =>
        hub.Clients.Group(PlayHub.ParticipantGroup(participantId)).TeamChanged();

    public Task SessionClosedAsync(IReadOnlyCollection<Guid> teamIds) =>
        teamIds.Count == 0 ? Task.CompletedTask : hub.Clients.Groups([.. teamIds.Select(PlayHub.TeamGroup)]).SessionClosed();

    private IPlayClient Team(Guid teamId) => hub.Clients.Group(PlayHub.TeamGroup(teamId));
}
