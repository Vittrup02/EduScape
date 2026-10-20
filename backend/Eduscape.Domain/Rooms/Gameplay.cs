using Eduscape.Domain.Common;
using Eduscape.Domain.Sessions;

namespace Eduscape.Domain.Rooms;

public enum AnswerOutcome
{
    Correct,
    Wrong,

    /// <summary>
    /// The team had already answered this step (or the whole lock), e.g. a teammate answered at the same moment.
    /// Nothing is counted.
    /// </summary>
    AlreadySolved,

    /// <summary>The team answered wrong a moment ago and must wait; nothing is counted.</summary>
    CoolingDown,
}

/// <param name="Outcome">Whether the answer solved the step.</param>
/// <param name="HintRevealedNow">This wrong answer was the one that made the step's hint visible to the team.</param>
/// <param name="SolvedSteps">How many of the lock's steps the team has answered now.</param>
/// <param name="LockSolved">This answer solved the last step, and with it the lock.</param>
/// <param name="OpenedDoorwayIds">Doors and exit doors that opened because of this answer.</param>
/// <param name="CooldownSeconds">How long the team must wait before answering the lock again.</param>
public sealed record AnswerResult(
    AnswerOutcome Outcome,
    bool HintRevealedNow,
    int SolvedSteps,
    bool LockSolved,
    IReadOnlyList<Guid> OpenedDoorwayIds,
    int CooldownSeconds);

/// <summary>
/// What happens when a team plays: answering locks step by step, and walking out through the exit door. Every action
/// is checked against the rules in <see cref="RoomProgress"/>, so a team can only use objects it can see. Doors are
/// never opened by hand: they open by themselves when an answer meets their condition.
/// </summary>
public static class Gameplay
{
    /// <summary>
    /// Checks an answer to the lock's step <paramref name="stepIndex"/>, which must be the step the team is on;
    /// otherwise a teammate got there first and the answer changes nothing. After a wrong answer the lock stays
    /// locked for the room's <see cref="EscapeRoom.WrongAnswerCooldownSeconds"/>.
    /// </summary>
    public static AnswerResult SubmitAnswer(
        EscapeRoom room, Team team, Guid lockId, int stepIndex, AnswerSubmission submission, DateTimeOffset now)
    {
        var @lock = FindAvailable<Lock>(room, team, lockId);
        if (@lock.Steps.Count == 0)
        {
            throw new DomainException(DomainErrors.RoomInvalid);
        }

        // Blank, oversized or malformed answers are rejected without counting as an attempt.
        var step = @lock.Steps[Math.Clamp(stepIndex, 0, @lock.Steps.Count - 1)];
        Validate(step, submission);

        var solvedSteps = team.SolvedSteps(lockId);
        if (team.IsLockSolved(lockId) || stepIndex != solvedSteps)
        {
            return new AnswerResult(AnswerOutcome.AlreadySolved, false, solvedSteps, false, [], 0);
        }

        var waiting = team.CooldownRemainingSeconds(lockId, room.WrongAnswerCooldownSeconds, now);
        if (waiting > 0)
        {
            return new AnswerResult(AnswerOutcome.CoolingDown, false, solvedSteps, false, [], waiting);
        }

        if (AnswerMatcher.IsCorrect(step, submission))
        {
            var openBefore = RoomProgress.For(room, team).OpenDoorwayIds;
            var lockSolved = team.SolveStep(lockId, @lock.Steps.Count, now);
            IReadOnlyList<Guid> opened = lockSolved
                ? [.. RoomProgress.For(room, team).OpenDoorwayIds.Where(id => !openBefore.Contains(id))]
                : [];
            return new AnswerResult(AnswerOutcome.Correct, false, team.SolvedSteps(lockId), lockSolved, opened, 0);
        }

        var hintWasVisible = team.IsHintVisible(lockId);
        team.RegisterWrongAttempt(lockId, now);
        var hintRevealedNow = step.Hint is not null && !hintWasVisible && team.IsHintVisible(lockId);
        return new AnswerResult(
            AnswerOutcome.Wrong,
            hintRevealedNow,
            solvedSteps,
            false,
            [],
            team.CooldownRemainingSeconds(lockId, room.WrongAnswerCooldownSeconds, now));
    }

    /// <summary>
    /// Lets the team escape through an open exit door. Returns false if the team had already escaped.
    /// </summary>
    public static bool OpenExitDoor(EscapeRoom room, Team team, Guid exitDoorId, DateTimeOffset now)
    {
        var exitDoor = FindAvailable<ExitDoor>(room, team, exitDoorId);
        if (team.EscapedAt is not null)
        {
            return false;
        }

        if (!RoomProgress.For(room, team).CanOpenExitDoor(exitDoor))
        {
            throw new DomainException(DomainErrors.ExitDoorLocked);
        }

        team.MarkEscaped(now);
        return true;
    }

    /// <summary>
    /// Records the scene a student moved to, for the teacher's dashboard. Students move freely between the scenes
    /// their team has unlocked, so only those can be reported. Returns false if the student was already there.
    /// </summary>
    public static bool EnterScene(EscapeRoom room, Team team, Participant participant, Guid sceneId, DateTimeOffset now)
    {
        if (room.Scenes.All(s => s.Id != sceneId))
        {
            throw new DomainException(DomainErrors.SceneNotFound);
        }

        if (!RoomProgress.For(room, team).IsSceneUnlocked(sceneId))
        {
            throw new DomainException(DomainErrors.SceneLocked);
        }

        return participant.EnterScene(sceneId, now);
    }

    /// <summary>
    /// For a teacher previewing the room from a scene: solves the locks the team can reach outside that scene until the
    /// scene opens, so the preview starts there as if the scenes before it were done. The scene's own locks stay unsolved.
    /// </summary>
    public static void SolveUntilSceneOpens(EscapeRoom room, Team team, Guid sceneId, DateTimeOffset now)
    {
        if (room.Scenes.All(s => s.Id != sceneId))
        {
            throw new DomainException(DomainErrors.SceneNotFound);
        }

        while (true)
        {
            var progress = RoomProgress.For(room, team);
            if (progress.IsSceneUnlocked(sceneId))
            {
                return;
            }

            var solvable = room.Objects.OfType<Lock>()
                .Where(l => l.SceneId != sceneId && !team.IsLockSolved(l.Id) && progress.IsAvailable(l))
                .ToList();
            if (solvable.Count == 0)
            {
                // The scene can't be reached; Validate reports that, and the preview then starts where it can.
                return;
            }

            foreach (var @lock in solvable)
            {
                team.TrySolveLock(@lock.Id, now, @lock.Steps.Count);
            }
        }
    }

    private static void Validate(LockStep step, AnswerSubmission submission)
    {
        if (step.AnswerType != AnswerType.Order)
        {
            Guard.Required(submission.Text, TextLimits.Answer);
            return;
        }

        if (submission.Order is not { Count: > 0 } order)
        {
            throw new DomainException(DomainErrors.ValueRequired);
        }

        if (order.Count > LockStep.MaxOrderItems)
        {
            throw new DomainException(DomainErrors.ListTooLong);
        }
    }

    private static T FindAvailable<T>(EscapeRoom room, Team team, Guid objectId)
        where T : RoomObject
    {
        var roomObject = room.Objects.OfType<T>().SingleOrDefault(o => o.Id == objectId)
            ?? throw new DomainException(DomainErrors.ObjectNotFound);

        if (!RoomProgress.For(room, team).IsAvailable(roomObject))
        {
            throw new DomainException(DomainErrors.ObjectNotAvailable);
        }

        return roomObject;
    }
}
