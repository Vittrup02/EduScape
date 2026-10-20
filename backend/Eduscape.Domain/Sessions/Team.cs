namespace Eduscape.Domain.Sessions;

/// <summary>
/// A team in a session. Progress (solved locks, opened doors) is shared by the whole team and owned by the server;
/// navigation between unlocked scenes is individual (each <see cref="Participant"/> reports its own scene).
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

    /// <summary>
    /// How many students were on the team, recorded when their data is erased after the session (see
    /// <see cref="EraseParticipants"/>). Null while the participants are still stored.
    /// </summary>
    public int? ParticipantCount { get; private set; }

    public IReadOnlyList<TeamLockProgress> LockProgress => _lockProgress;

    public IReadOnlyList<TeamOpenedDoor> OpenedDoors => _openedDoors;

    public IReadOnlyList<Participant> Participants => _participants;

    public IReadOnlySet<Guid> SolvedLockIds =>
        _lockProgress.Where(p => p.IsSolved).Select(p => p.LockId).ToHashSet();

    public IReadOnlySet<Guid> OpenedDoorIds => _openedDoors.Select(d => d.DoorId).ToHashSet();

    public Participant AddParticipant(string? nickname, string deviceTokenHash, DateTimeOffset joinedAt)
    {
        var participant = new Participant(Id, nickname, deviceTokenHash, joinedAt);
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

    public void RegisterWrongAttempt(Guid lockId, DateTimeOffset now)
    {
        var progress = GetOrAddProgress(lockId);
        if (!progress.IsSolved)
        {
            progress.WrongAttempts++;
            progress.FirstAttemptAt ??= now;
            progress.LastAttemptAt = now;
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

    /// <summary>When the team last solved a lock or opened a door; null before its first progress.</summary>
    public DateTimeOffset? LastProgressAt =>
        _lockProgress.Select(p => p.SolvedAt)
            .Concat(_openedDoors.Select(d => (DateTimeOffset?)d.OpenedAt))
            .Max();

    /// <summary>
    /// The unsolved lock the team tried last, for the teacher's dashboard: since when the team has been working on it
    /// without progress (its first attempt, or the team's last progress if that came later) and how many wrong
    /// answers it has given. Null when the team has not tried an unsolved lock, or once it has escaped.
    /// </summary>
    public CurrentLock? FindCurrentLock()
    {
        if (EscapedAt is not null)
        {
            return null;
        }

        var current = _lockProgress
            .Where(p => !p.IsSolved && p.LastAttemptAt is not null)
            .MaxBy(p => p.LastAttemptAt);
        if (current?.FirstAttemptAt is not { } firstAttemptAt)
        {
            return null;
        }

        var stuckSince = LastProgressAt is { } lastProgressAt && lastProgressAt > firstAttemptAt
            ? lastProgressAt
            : firstAttemptAt;
        return new CurrentLock(current.LockId, stuckSince, current.WrongAttempts);
    }

    /// <summary>
    /// Deletes the team's students (nicknames and device tokens) and keeps only how many there were. The team's
    /// progress stays, without names, as the teacher's history.
    /// </summary>
    public void EraseParticipants()
    {
        ParticipantCount = (ParticipantCount ?? 0) + _participants.Count;
        _participants.Clear();
    }

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
