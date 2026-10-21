namespace Eduscape.Domain.Common;

/// <summary>Error codes returned to the frontend, which translates them.</summary>
public static class DomainErrors
{
    public const string ValueRequired = "VALUE_REQUIRED";
    public const string ValueTooLong = "VALUE_TOO_LONG";
    public const string PositionOutOfRange = "POSITION_OUT_OF_RANGE";

    public const string LockToleranceInvalid = "LOCK_TOLERANCE_INVALID";

    /// <summary>A step setting that does not fit its answer type, or a broken step; the editor never sends these.</summary>
    public const string LockStepInvalid = "LOCK_STEP_INVALID";

    /// <summary>Too many steps, options, accepted answers or order items.</summary>
    public const string ListTooLong = "LIST_TOO_LONG";

    public const string DoorConditionInvalid = "DOOR_CONDITION_INVALID";

    /// <summary>Doors open by their condition; they cannot wait for anything with unlockedBy.</summary>
    public const string DoorCannotWait = "DOOR_CANNOT_WAIT";

    /// <summary>The wait after a wrong answer must be between 0 and <see cref="Rooms.EscapeRoom.MaxWrongAnswerCooldownSeconds"/>.</summary>
    public const string CooldownInvalid = "COOLDOWN_INVALID";

    // Thrown for options on a lock that is not multiple choice; otherwise reported by EscapeRoom.Validate.
    public const string LockOptionsInvalid = "LOCK_OPTIONS_INVALID";
    public const string LockAnswerNotInOptions = "LOCK_ANSWER_NOT_IN_OPTIONS";

    public const string SceneNotFound = "SCENE_NOT_FOUND";
    public const string LastScene = "LAST_SCENE";
    public const string ImageKeyInvalid = "IMAGE_KEY_INVALID";

    /// <summary>The room has problems reported by <see cref="Rooms.EscapeRoom.Validate"/> and cannot be played.</summary>
    public const string RoomInvalid = "ROOM_INVALID";

    public const string SessionPinInvalid = "SESSION_PIN_INVALID";
    public const string SessionClosed = "SESSION_CLOSED";
    public const string SessionLocked = "SESSION_LOCKED";
    public const string PinExpired = "PIN_EXPIRED";
    public const string TeamNotFound = "TEAM_NOT_FOUND";
    public const string TeamCountInvalid = "TEAM_COUNT_INVALID";
    public const string ParticipantNotFound = "PARTICIPANT_NOT_FOUND";

    /// <summary>Students cannot be moved in individual mode, where every team is one student.</summary>
    public const string ParticipantMoveNotAllowed = "PARTICIPANT_MOVE_NOT_ALLOWED";

    // Playing
    public const string ObjectNotFound = "OBJECT_NOT_FOUND";
    public const string ObjectNotAvailable = "OBJECT_NOT_AVAILABLE";
    public const string ExitDoorLocked = "EXIT_DOOR_LOCKED";
    public const string SceneLocked = "SCENE_LOCKED";

    // Room validation (reported by EscapeRoom.Validate, not thrown)
    public const string StartSceneMissing = "START_SCENE_MISSING";
    public const string UnlockedByNotFound = "UNLOCKED_BY_NOT_FOUND";
    public const string UnlockedBySelf = "UNLOCKED_BY_SELF";
    public const string UnlockedByInvalidTarget = "UNLOCKED_BY_INVALID_TARGET";
    public const string UnlockedByCycle = "UNLOCKED_BY_CYCLE";
    public const string DoorTargetMissing = "DOOR_TARGET_MISSING";
    public const string LockQuestionMissing = "LOCK_QUESTION_MISSING";
    public const string LockAnswerMissing = "LOCK_ANSWER_MISSING";
    public const string LetterTextMissing = "LETTER_TEXT_MISSING";
    public const string TextBoxTextMissing = "TEXT_BOX_TEXT_MISSING";
    public const string ExitDoorMissing = "EXIT_DOOR_MISSING";
    public const string LockPuzzleMissing = "LOCK_PUZZLE_MISSING";

    /// <summary>An order step needs at least two items, each with a text.</summary>
    public const string OrderItemsInvalid = "ORDER_ITEMS_INVALID";

    /// <summary>A door that opens with selected locks has none chosen, or one that is no longer in the room.</summary>
    public const string DoorLocksMissing = "DOOR_LOCKS_MISSING";

    /// <summary>A lock or exit door students can never reach, so they can never escape.</summary>
    public const string ObjectUnreachable = "OBJECT_UNREACHABLE";
}
