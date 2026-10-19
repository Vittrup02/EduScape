namespace Eduscape.Domain.Common;

/// <summary>Error codes returned to the frontend, which translates them.</summary>
public static class DomainErrors
{
    public const string ValueRequired = "VALUE_REQUIRED";
    public const string ValueTooLong = "VALUE_TOO_LONG";
    public const string PositionOutOfRange = "POSITION_OUT_OF_RANGE";

    public const string LockToleranceInvalid = "LOCK_TOLERANCE_INVALID";
    public const string LockOptionsInvalid = "LOCK_OPTIONS_INVALID";
    public const string LockAnswerNotInOptions = "LOCK_ANSWER_NOT_IN_OPTIONS";

    public const string SceneNotFound = "SCENE_NOT_FOUND";

    public const string SessionPinInvalid = "SESSION_PIN_INVALID";
    public const string SessionClosed = "SESSION_CLOSED";

    // Room validation (reported by EscapeRoom.Validate, not thrown)
    public const string StartSceneMissing = "START_SCENE_MISSING";
    public const string UnlockedByNotFound = "UNLOCKED_BY_NOT_FOUND";
    public const string UnlockedBySelf = "UNLOCKED_BY_SELF";
    public const string UnlockedByInvalidTarget = "UNLOCKED_BY_INVALID_TARGET";
    public const string UnlockedByCycle = "UNLOCKED_BY_CYCLE";
    public const string DoorTargetMissing = "DOOR_TARGET_MISSING";
}
