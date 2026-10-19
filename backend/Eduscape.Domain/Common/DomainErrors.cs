namespace Eduscape.Domain.Common;

/// <summary>Error codes returned to the frontend, which translates them.</summary>
public static class DomainErrors
{
    public const string ValueRequired = "VALUE_REQUIRED";
    public const string ValueTooLong = "VALUE_TOO_LONG";
    public const string PositionOutOfRange = "POSITION_OUT_OF_RANGE";

    public const string LockToleranceInvalid = "LOCK_TOLERANCE_INVALID";

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

    // Playing
    public const string ObjectNotFound = "OBJECT_NOT_FOUND";
    public const string ObjectNotAvailable = "OBJECT_NOT_AVAILABLE";
    public const string ExitDoorLocked = "EXIT_DOOR_LOCKED";

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
}
