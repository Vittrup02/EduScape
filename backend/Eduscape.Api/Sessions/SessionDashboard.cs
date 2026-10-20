using Eduscape.Api.Play;
using Eduscape.Domain.Rooms;
using Eduscape.Domain.Sessions;
using Eduscape.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Eduscape.Api.Sessions;

public sealed record DashboardSceneDto(Guid Id, string Name);

/// <summary>A lock as the teacher's dashboard shows it: its question, never its answer.</summary>
public sealed record DashboardLockDto(Guid Id, Guid SceneId, string Question);

/// <summary><c>SceneId</c> is the scene the student's device last reported, or the start scene before that.</summary>
public sealed record DashboardParticipantDto(Guid Id, string? Nickname, Guid? SceneId, DateTimeOffset? SceneEnteredAt);

/// <summary>The unsolved lock the team tried last (see <see cref="Team.FindCurrentLock"/>).</summary>
public sealed record DashboardCurrentLockDto(Guid LockId, DateTimeOffset StuckSince, int WrongAttempts);

/// <summary>
/// A team's progress. <c>Participants</c> is empty once the students' data has been erased after the session;
/// <c>ParticipantCount</c> still says how many played.
/// </summary>
public sealed record DashboardTeamDto(
    Guid Id,
    TeamColor Color,
    Guid[] SolvedLockIds,
    DateTimeOffset? EscapedAt,
    DashboardCurrentLockDto? CurrentLock,
    int ParticipantCount,
    DashboardParticipantDto[] Participants);

/// <summary>
/// Everything the teacher's live dashboard shows, sent whole on every change. <c>ServerNow</c> lets the dashboard
/// count minutes with the server's clock.
/// </summary>
public sealed record SessionDashboardDto(
    Guid Id,
    string Pin,
    SessionMode Mode,
    SessionStatus Status,
    string RoomTitle,
    string ClassName,
    DateTimeOffset StartedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? ClosedAt,
    DateTimeOffset ServerNow,
    DashboardSceneDto[] Scenes,
    DashboardLockDto[] Locks,
    DashboardTeamDto[] Teams);

internal static class SessionDashboard
{
    /// <summary>The dashboard of one of the teacher's sessions, or null if it is not theirs. Closes it if expired.</summary>
    public static async Task<SessionDashboardDto?> LoadAsync(
        Guid id, Guid teacherId, EduscapeDbContext db, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var session = await TeacherSessionEndpoints.OwnSessions(db, teacherId)
            .Include(s => s.Teams).ThenInclude(t => t.Participants)
            .Include(s => s.Teams).ThenInclude(t => t.LockProgress)
            .Include(s => s.Teams).ThenInclude(t => t.OpenedDoors)
            .AsSplitQuery()
            .SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (session is null)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        if (session.CloseIfExpired(now))
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        var room = await TeamActions.LoadRoomAsync(db, session.EscapeRoomId, cancellationToken);
        var className = await db.Classes
            .Where(c => c.Id == session.ClassId)
            .Select(c => c.Name)
            .SingleAsync(cancellationToken);

        var scenes = room.Scenes.OrderBy(s => s.SortOrder).ToArray();
        var locks = scenes
            .SelectMany(s => s.Objects.OfType<Lock>().OrderBy(l => l.Id))
            .Select(l => new DashboardLockDto(l.Id, l.SceneId, l.Question))
            .ToArray();
        var startSceneId = room.StartScene?.Id;

        var teams = session.Teams
            .OrderBy(t => t.Color)
            .ThenBy(t => t.Id)
            .Select(t => new DashboardTeamDto(
                t.Id,
                t.Color,
                [.. t.SolvedLockIds],
                t.EscapedAt,
                t.FindCurrentLock() is { } current
                    ? new DashboardCurrentLockDto(current.LockId, current.StuckSince, current.WrongAttempts)
                    : null,
                t.ParticipantCount ?? t.Participants.Count,
                [.. t.Participants
                    .OrderBy(p => p.JoinedAt)
                    .Select(p => new DashboardParticipantDto(
                        p.Id, p.Nickname, p.CurrentSceneId ?? startSceneId, p.SceneEnteredAt ?? p.JoinedAt))]))
            .ToArray();

        return new SessionDashboardDto(
            session.Id,
            session.Pin,
            session.Mode,
            session.Status,
            room.Title,
            className,
            session.StartedAt,
            session.ExpiresAt,
            session.ClosedAt,
            now,
            [.. scenes.Select(s => new DashboardSceneDto(s.Id, s.Name))],
            locks,
            teams);
    }
}
