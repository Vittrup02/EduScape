namespace Eduscape.Domain.Sessions;

/// <summary>A team's progress on one lock: wrong attempts (for the hint) and when it was solved.</summary>
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

    public int WrongAttempts { get; internal set; }

    public DateTimeOffset? SolvedAt { get; internal set; }

    public bool IsSolved => SolvedAt is not null;
}
