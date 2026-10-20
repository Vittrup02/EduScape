using Eduscape.Domain.Common;
using Eduscape.Domain.Rooms;
using Eduscape.Domain.Sessions;
using Eduscape.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Eduscape.Api.Play;

/// <summary>The outcome of a <see cref="TeamActions.RunAsync{TResult}"/> call, with the team and room it ran on.</summary>
public sealed record TeamActionResult<TResult>(TResult Result, Team Team, EscapeRoom Room);

/// <summary>
/// Runs a student's action (answering a lock, opening a door) on their team's progress, which the server owns.
/// Actions on the same team run one at a time: the team's row is locked for the transaction. So when two students
/// solve the same lock at once, the second sees it solved – one registration, no error.
/// </summary>
public sealed class TeamActions(EduscapeDbContext db, TimeProvider timeProvider)
{
    /// <summary>
    /// Runs <paramref name="action"/> on the device's team and saves the result. Returns null if the device token is
    /// unknown; throws <see cref="DomainErrors.SessionClosed"/> once the session is closed or expired. A locked session
    /// can still be played by the students who are in.
    /// </summary>
    public Task<TeamActionResult<TResult>?> RunAsync<TResult>(
        string? deviceToken,
        Func<EscapeRoom, Team, DateTimeOffset, TResult> action,
        CancellationToken cancellationToken) =>
        RunAsync(deviceToken, (room, team, _, now) => action(room, team, now), cancellationToken);

    /// <inheritdoc cref="RunAsync{TResult}(string?, Func{EscapeRoom, Team, DateTimeOffset, TResult}, CancellationToken)"/>
    /// <remarks>For actions that also change the student's own <see cref="Participant"/>.</remarks>
    public async Task<TeamActionResult<TResult>?> RunAsync<TResult>(
        string? deviceToken,
        Func<EscapeRoom, Team, Participant, DateTimeOffset, TResult> action,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(deviceToken))
        {
            return null;
        }

        var hash = DeviceToken.Hash(deviceToken);
        var participant = await db.Participants.SingleOrDefaultAsync(p => p.DeviceTokenHash == hash, cancellationToken);
        if (participant is null)
        {
            return null;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlAsync(
            $"""SELECT 1 FROM "Teams" WHERE "Id" = {participant.TeamId} FOR UPDATE""", cancellationToken);

        var session = await db.Sessions.SingleAsync(s => s.Teams.Any(t => t.Id == participant.TeamId), cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (session.CloseIfExpired(now))
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        if (session.Status == SessionStatus.Closed)
        {
            throw new DomainException(DomainErrors.SessionClosed);
        }

        var team = await LoadTeamAsync(db, participant.TeamId, cancellationToken);
        var room = await LoadRoomAsync(db, session.EscapeRoomId, cancellationToken);

        var result = action(room, team, participant, now);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new TeamActionResult<TResult>(result, team, room);
    }

    public static Task<Team> LoadTeamAsync(EduscapeDbContext db, Guid teamId, CancellationToken cancellationToken) =>
        db.Teams
            .Include(t => t.LockProgress)
            .AsSplitQuery()
            .SingleAsync(t => t.Id == teamId, cancellationToken);

    /// <summary>The room with its scenes and objects, read-only.</summary>
    public static Task<EscapeRoom> LoadRoomAsync(EduscapeDbContext db, Guid roomId, CancellationToken cancellationToken) =>
        db.EscapeRooms
            .AsNoTracking()
            .Include(r => r.Scenes)
            .ThenInclude(s => s.Objects)
            .AsSplitQuery()
            .SingleAsync(r => r.Id == roomId, cancellationToken);
}
