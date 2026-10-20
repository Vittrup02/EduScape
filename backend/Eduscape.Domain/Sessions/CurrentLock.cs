namespace Eduscape.Domain.Sessions;

/// <summary>The unsolved lock a team is working on (see <see cref="Team.FindCurrentLock"/>).</summary>
/// <param name="StuckSince">Since when the team has made no progress while working on the lock.</param>
public sealed record CurrentLock(Guid LockId, DateTimeOffset StuckSince, int WrongAttempts);
