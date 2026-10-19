namespace Eduscape.Domain.Sessions;

/// <summary>
/// A team in a session. Progress (solved locks, opened doors) is shared by the whole team and owned by the server;
/// navigation between unlocked scenes is individual and not stored.
/// </summary>
public sealed class Team
{
    public const int WrongAttemptsBeforeHint = 3;

    private readonly List<TeamLockProgress> _lockProgress = [];
    private readonly List<TeamOpenedDoor> _openedDoors = [];
    private readonly List<Participant> _participants = [];

    internal Team(Guid sessionId, TeamColor color)
    {
        SessionId = sessionId;
        Color = color;
    }

    // For EF Core
    private Team()
    {
    }

    public Guid Id { get; private set; } = Guid.CreateVersion7();

    public Guid SessionId { get; private set; }

    public TeamColor Color { get; private set; }

    /// <summary>When the team opened the exit door. Kept (without nicknames) for the teacher's history.</summary>
    public DateTimeOffset? EscapedAt { get; private set; }

    public IReadOnlyList<TeamLockProgress> LockProgress => _lockProgress;

    public IReadOnlyList<TeamOpenedDoor> OpenedDoors => _openedDoors;

    public IReadOnlyList<Participant> Participants => _participants;

    public IReadOnlySet<Guid> SolvedLockIds =>
        _lockProgress.Where(p => p.IsSolved).Select(p => p.LockId).ToHashSet();

    public IReadOnlySet<Guid> OpenedDoorIds => _openedDoors.Select(d => d.DoorId).ToHashSet();

    public Participant AddParticipant(string nickname, string deviceToken, DateTimeOffset joinedAt)
    {
        var participant = new Participant(Id, nickname, deviceToken, joinedAt);
        _participants.Add(participant);
        return participant;
    }

    public void MoveParticipantTo(Participant participant, Team target)
    {
        if (_participants.Remove(participant))
        {
            participant.TeamId = target.Id;
            target._participants.Add(participant);
        }
    }

    /// <summary>
    /// Marks the lock as solved. Idempotent: returns false (and changes nothing) if the team already solved it,
    /// e.g. when two students answer at the same time.
    /// </summary>
    public bool TrySolveLock(Guid lockId, DateTimeOffset now)
    {
        var progress = GetOrAddProgress(lockId);
        if (progress.IsSolved)
        {
            return false;
        }

        progress.SolvedAt = now;
        return true;
    }

    public void RegisterWrongAttempt(Guid lockId)
    {
        var progress = GetOrAddProgress(lockId);
        if (!progress.IsSolved)
        {
            progress.WrongAttempts++;
        }
    }

    public bool IsLockSolved(Guid lockId) => FindProgress(lockId)?.IsSolved ?? false;

    public int WrongAttempts(Guid lockId) => FindProgress(lockId)?.WrongAttempts ?? 0;

    public bool IsHintVisible(Guid lockId) => WrongAttempts(lockId) >= WrongAttemptsBeforeHint;

    /// <summary>Records that the team opened a door. Idempotent: returns false if it was already open.</summary>
    public bool OpenDoor(Guid doorId, DateTimeOffset now)
    {
        if (_openedDoors.Any(d => d.DoorId == doorId))
        {
            return false;
        }

        _openedDoors.Add(new TeamOpenedDoor(Id, doorId, now));
        return true;
    }

    /// <summary>Records that the team escaped. Idempotent: the first time is kept.</summary>
    public void MarkEscaped(DateTimeOffset now) => EscapedAt ??= now;

    private TeamLockProgress? FindProgress(Guid lockId) => _lockProgress.Find(p => p.LockId == lockId);

    private TeamLockProgress GetOrAddProgress(Guid lockId)
    {
        var progress = FindProgress(lockId);
        if (progress is null)
        {
            progress = new TeamLockProgress(Id, lockId);
            _lockProgress.Add(progress);
        }

        return progress;
    }
}
