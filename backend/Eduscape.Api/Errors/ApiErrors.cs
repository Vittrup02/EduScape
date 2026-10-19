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
