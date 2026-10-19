namespace Eduscape.Api.Errors;

/// <summary>
/// HTTP-level error codes returned to the frontend, which translates them.
/// Domain-specific codes live in <see cref="Domain.Common.DomainErrors"/>.
/// </summary>
public static class ApiErrors
{
    public const string BadRequest = "BAD_REQUEST";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string Forbidden = "FORBIDDEN";
    public const string NotFound = "NOT_FOUND";
    public const string TooManyRequests = "TOO_MANY_REQUESTS";
    public const string InternalError = "INTERNAL_ERROR";

    // Teacher accounts. Wrong password and unknown e-mail share one code, so login does not reveal accounts.
    public const string InvalidCredentials = "INVALID_CREDENTIALS";
    public const string LoginLockedOut = "LOGIN_LOCKED_OUT";
    public const string EmailTaken = "EMAIL_TAKEN";
    public const string EmailInvalid = "EMAIL_INVALID";
    public const string PasswordTooShort = "PASSWORD_TOO_SHORT";
    public const string PasswordTooLong = "PASSWORD_TOO_LONG";

    // Students. A closed session's PIN is reported as not found: it may already belong to a new session.
    public const string PinNotFound = "PIN_NOT_FOUND";
    public const string DeviceNotRecognized = "DEVICE_NOT_RECOGNIZED";

    // Room editor. A save based on an older revision is refused, so two open editors cannot overwrite each other.
    public const string RoomChanged = "ROOM_CHANGED";
    public const string IdTaken = "ID_TAKEN";
    public const string DocumentInvalid = "DOCUMENT_INVALID";
    public const string RoomTooLarge = "ROOM_TOO_LARGE";

    // Image uploads.
    public const string UploadsUnavailable = "UPLOADS_UNAVAILABLE";
    public const string UploadNotFound = "UPLOAD_NOT_FOUND";
    public const string ImageTypeNotAllowed = "IMAGE_TYPE_NOT_ALLOWED";
    public const string ImageTooLarge = "IMAGE_TOO_LARGE";
    public const string ImageInvalid = "IMAGE_INVALID";

    /// <summary>Generic code for an error response that has no more specific code.</summary>
    public static string FromStatusCode(int statusCode) => statusCode switch
    {
        StatusCodes.Status401Unauthorized => Unauthorized,
        StatusCodes.Status403Forbidden => Forbidden,
        StatusCodes.Status404NotFound => NotFound,
        StatusCodes.Status429TooManyRequests => TooManyRequests,
        >= StatusCodes.Status500InternalServerError => InternalError,
        _ => BadRequest,
    };
}
