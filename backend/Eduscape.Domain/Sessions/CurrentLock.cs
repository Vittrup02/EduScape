namespace Eduscape.Domain.Sessions;

/// <summary>The unsolved lock a team is working on (see <see cref="Team.FindCurrentLock"/>).</summary>
/// <param name="StuckSince">Since when the team has made no progress while working on the lock's current step.</param>
/// <param name="WrongAttempts">Wrong answers on the lock, over all its steps.</param>
/// <param name="SolvedSteps">The steps the team has answered, i.e. the index of the step it is stuck on.</param>
public sealed record CurrentLock(Guid LockId, DateTimeOffset StuckSince, int WrongAttempts, int SolvedSteps);
