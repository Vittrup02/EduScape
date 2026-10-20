using Eduscape.Domain.Common;
using Eduscape.Domain.Sessions;

namespace Eduscape.Domain.Rooms;

public enum AnswerOutcome
{
    Correct,
    Wrong,

    /// <summary>The team had already solved the lock, e.g. a teammate answered at the same moment.</summary>
    AlreadySolved,
}

/// <param name="Outcome">Whether the answer solved the lock.</param>
/// <param name="HintRevealedNow">This wrong answer was the one that made the lock's hint visible to the team.</param>
public sealed record AnswerResult(AnswerOutcome Outcome, bool HintRevealedNow);

/// <summary>
/// What happens when a team plays: answering locks and opening doors. Every action is checked against the
/// unlockedBy rules (<see cref="RoomProgress"/>), so a team can only use objects it can see.
/// </summary>
public static class Gameplay
{
    public static AnswerResult SubmitAnswer(EscapeRoom room, Team team, Guid lockId, string? answer, DateTimeOffset now)
    {
        var @lock = FindAvailable<Lock>(room, team, lockId);

        // Blank or oversized answers are rejected without counting as an attempt.
        var submitted = Guard.Required(answer, TextLimits.Answer);

        if (team.IsLockSolved(@lock.Id))
        {
            return new AnswerResult(AnswerOutcome.AlreadySolved, HintRevealedNow: false);
        }

        if (AnswerMatcher.IsCorrect(@lock, submitted))
        {
            team.TrySolveLock(@lock.Id, now);
            return new AnswerResult(AnswerOutcome.Correct, HintRevealedNow: false);
        }

        var hintWasVisible = team.IsHintVisible(@lock.Id);
        team.RegisterWrongAttempt(@lock.Id, now);
        var hintRevealedNow = @lock.Hint is not null && !hintWasVisible && team.IsHintVisible(@lock.Id);
        return new AnswerResult(AnswerOutcome.Wrong, hintRevealedNow);
    }

    /// <summary>Opens the door for the whole team. Returns false if the team had already opened it.</summary>
    public static bool OpenDoor(EscapeRoom room, Team team, Guid doorId, DateTimeOffset now)
    {
        var door = FindAvailable<Door>(room, team, doorId);
        return team.OpenDoor(door.Id, now);
    }

    /// <summary>
    /// Lets the team escape once every lock in the room is solved. Returns false if the team had already escaped.
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
    /// their team has unlocked, so only those can be reported.
    /// </summary>
    public static void EnterScene(EscapeRoom room, Team team, Participant participant, Guid sceneId, DateTimeOffset now)
    {
        if (room.Scenes.All(s => s.Id != sceneId))
        {
            throw new DomainException(DomainErrors.SceneNotFound);
        }

        if (!RoomProgress.For(room, team).IsSceneUnlocked(sceneId))
        {
            throw new DomainException(DomainErrors.SceneLocked);
        }

        participant.EnterScene(sceneId, now);
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
