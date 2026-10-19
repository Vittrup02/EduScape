using Eduscape.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;

namespace Eduscape.Api.Errors;

/// <summary>Turns exceptions into <see cref="ApiError"/> responses carrying only an error code.</summary>
public sealed class ErrorCodeExceptionHandler(ILogger<ErrorCodeExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (statusCode, code) = exception switch
        {
            DomainException domain => (StatusCodes.Status400BadRequest, domain.Code),
            BadHttpRequestException badRequest => (badRequest.StatusCode, ApiErrors.FromStatusCode(badRequest.StatusCode)),
            _ => (StatusCodes.Status500InternalServerError, ApiErrors.InternalError),
        };

        // Client errors are expected; only server errors are worth logging.
        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception");
        }

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(new ApiError(code), cancellationToken);
        return true;
    }
}
