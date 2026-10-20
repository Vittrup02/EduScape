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
    /// Marks the whole lock as solved, all steps included. Idempotent: returns false (and changes nothing) if the team
    /// already solved it, e.g. when two students answer at the same time.
    /// </summary>
    public bool TrySolveLock(Guid lockId, DateTimeOffset now, int stepCount = 1)
    {
        var progress = GetOrAddProgress(lockId);
        if (progress.IsSolved)
        {
            return false;
        }

        progress.SolvedSteps = Math.Max(progress.SolvedSteps, stepCount);
        progress.SolvedAt = now;
        return true;
    }

    /// <summary>
    /// Records that the team answered the lock's current step correctly; after the last of <paramref name="stepCount"/>
    /// steps the lock is solved. The next step starts afresh: no wrong attempts, no hint and no wait.
    /// Returns true if this solved the whole lock.
    /// </summary>
    public bool SolveStep(Guid lockId, int stepCount, DateTimeOffset now)
    {
        var progress = GetOrAddProgress(lockId);
        if (progress.IsSolved)
        {
            return false;
        }

        progress.SolvedSteps++;
        progress.StepWrongAttempts = 0;
        progress.FirstAttemptAt = null;
        progress.LastAttemptAt = null;
        if (progress.SolvedSteps >= stepCount)
        {
            progress.SolvedAt = now;
            return true;
        }

        return false;
    }

    /// <summary>A wrong answer on the lock's current step; it counts for the hint, the wait and the dashboard.</summary>
    public void RegisterWrongAttempt(Guid lockId, DateTimeOffset now)
    {
        var progress = GetOrAddProgress(lockId);
        if (!progress.IsSolved)
        {
            progress.WrongAttempts++;
            progress.StepWrongAttempts++;
            progress.FirstAttemptAt ??= now;
            progress.LastAttemptAt = now;
        }
    }

    public bool IsLockSolved(Guid lockId) => FindProgress(lockId)?.IsSolved ?? false;

    /// <summary>How many of the lock's steps the team has answered; the index of the step it is on.</summary>
    public int SolvedSteps(Guid lockId) => FindProgress(lockId)?.SolvedSteps ?? 0;

    /// <summary>Wrong answers on the lock, over all its steps.</summary>
    public int WrongAttempts(Guid lockId) => FindProgress(lockId)?.WrongAttempts ?? 0;

    /// <summary>The current step's hint is shown after <see cref="WrongAttemptsBeforeHint"/> wrong answers on that step.</summary>
    public bool IsHintVisible(Guid lockId) => (FindProgress(lockId)?.StepWrongAttempts ?? 0) >= WrongAttemptsBeforeHint;

    /// <summary>
    /// How long the lock stays locked after the team's last wrong answer, rounded up to whole seconds; zero when the
    /// team may answer.
    /// </summary>
    public int CooldownRemainingSeconds(Guid lockId, int cooldownSeconds, DateTimeOffset now)
    {
        if (cooldownSeconds <= 0 || FindProgress(lockId) is not { IsSolved: false, LastAttemptAt: { } lastWrong })
        {
            return 0;
        }

        var remaining = lastWrong.AddSeconds(cooldownSeconds) - now;
        return remaining > TimeSpan.Zero ? (int)Math.Ceiling(remaining.TotalSeconds) : 0;
    }

    /// <summary>Records that the team escaped. Idempotent: the first time is kept.</summary>
    public void MarkEscaped(DateTimeOffset now) => EscapedAt ??= now;

    /// <summary>When the team last solved a lock; null before its first progress. Doors open with the locks.</summary>
    public DateTimeOffset? LastProgressAt => _lockProgress.Select(p => p.SolvedAt).Max();

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
        return new CurrentLock(current.LockId, stuckSince, current.WrongAttempts, current.SolvedSteps);
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
