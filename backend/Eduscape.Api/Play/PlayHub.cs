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
    Task LockSolved(Guid lockId);

    /// <summary>The team has answered the lock wrong <see cref="Team.WrongAttemptsBeforeHint"/> times.</summary>
    Task HintRevealed(Guid lockId, string hint);

    Task DoorOpened(Guid doorId);

    Task TeamEscaped();
}

/// <summary>
/// Realtime channel for students. Each team is a SignalR group. Students only listen here; actions go through the
/// play endpoints, which push the resulting events to the team's group.
/// </summary>
public sealed class PlayHub(EduscapeDbContext db, TimeProvider timeProvider) : Hub<IPlayClient>
{
    public const string Path = "/hubs/play";

    public static string TeamGroup(Guid teamId) => $"team:{teamId}";

    /// <summary>
    /// Adds this connection to the device's team. The token is sent as a message, not in the URL, so it stays out of
    /// logs. Returns null on success, or an error code: <see cref="ApiErrors.DeviceNotRecognized"/> or
    /// <see cref="DomainErrors.SessionClosed"/>. Clients call it again after every reconnect, because a new
    /// connection starts without groups, and then fetch the team's whole state.
    /// </summary>
    public async Task<string?> Subscribe(string? deviceToken)
    {
        if (string.IsNullOrEmpty(deviceToken))
        {
            return ApiErrors.DeviceNotRecognized;
        }

        var hash = DeviceToken.Hash(deviceToken);
        var teamId = await db.Participants
            .Where(p => p.DeviceTokenHash == hash)
            .Select(p => (Guid?)p.TeamId)
            .SingleOrDefaultAsync(Context.ConnectionAborted);
        if (teamId is null)
        {
            return ApiErrors.DeviceNotRecognized;
        }

        var session = await db.Sessions.SingleAsync(s => s.Teams.Any(t => t.Id == teamId), Context.ConnectionAborted);
        if (!session.IsActive(timeProvider.GetUtcNow()))
        {
            return DomainErrors.SessionClosed;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, TeamGroup(teamId.Value), Context.ConnectionAborted);
        return null;
    }
}

/// <summary>Pushes a team's progress to its devices. An interface so tests can count the events.</summary>
public interface ITeamNotifier
{
    Task LockSolvedAsync(Guid teamId, Guid lockId);

    Task HintRevealedAsync(Guid teamId, Guid lockId, string hint);

    Task DoorOpenedAsync(Guid teamId, Guid doorId);

    Task TeamEscapedAsync(Guid teamId);
}

internal sealed class SignalRTeamNotifier(IHubContext<PlayHub, IPlayClient> hub) : ITeamNotifier
{
    public Task LockSolvedAsync(Guid teamId, Guid lockId) => Team(teamId).LockSolved(lockId);

    public Task HintRevealedAsync(Guid teamId, Guid lockId, string hint) => Team(teamId).HintRevealed(lockId, hint);

    public Task DoorOpenedAsync(Guid teamId, Guid doorId) => Team(teamId).DoorOpened(doorId);

    public Task TeamEscapedAsync(Guid teamId) => Team(teamId).TeamEscaped();

    private IPlayClient Team(Guid teamId) => hub.Clients.Group(PlayHub.TeamGroup(teamId));
}
