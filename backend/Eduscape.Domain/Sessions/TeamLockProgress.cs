namespace Eduscape.Domain.Sessions;

/// <summary>
/// A team's progress on one lock: the steps answered, wrong attempts (for the hint, the wait after a wrong answer and
/// the dashboard) and when it was solved. The attempt times describe the current step: they start over with each step.
/// </summary>
public sealed class TeamLockProgress
{
    internal TeamLockProgress(Guid teamId, Guid lockId)
    {
        TeamId = teamId;
        LockId = lockId;
    }

    // For EF Core
    private TeamLockProgress()
    {
    }

    public Guid TeamId { get; private set; }

    public Guid LockId { get; private set; }

    /// <summary>Wrong answers over all the lock's steps, for the teacher's dashboard.</summary>
    public int WrongAttempts { get; internal set; }

    /// <summary>How many of the lock's steps the team has answered; the lock is solved after the last one.</summary>
    public int SolvedSteps { get; internal set; }

    /// <summary>Wrong answers on the current step; the step's hint appears after three.</summary>
    public int StepWrongAttempts { get; internal set; }

    /// <summary>The team's first wrong answer; with <see cref="LastAttemptAt"/> it shows the teacher who is stuck.</summary>
    public DateTimeOffset? FirstAttemptAt { get; internal set; }

    public DateTimeOffset? LastAttemptAt { get; internal set; }

    public DateTimeOffset? SolvedAt { get; internal set; }

    public bool IsSolved => SolvedAt is not null;
}
