using System.Security.Claims;
using Eduscape.Api.Auth;
using Eduscape.Api.Errors;
using Eduscape.Api.Hosting;
using Eduscape.Api.Teachers;
using Eduscape.Domain.Rooms;
using Eduscape.Infrastructure.Images;
using Eduscape.Infrastructure.Storage;

namespace Eduscape.Api.Uploads;

public sealed record StartUploadRequest(string? ContentType, long Size);

/// <summary>The browser PUTs the file to <c>Url</c> with the same Content-Type, then completes the upload.</summary>
public sealed record StartUploadResponse(Guid UploadId, string Url);

public sealed record CompleteUploadRequest(string? ContentType);

/// <summary><c>ImageKey</c> goes into the room; <c>ImageUrl</c> shows the image right away.</summary>
public sealed record CompleteUploadResponse(string ImageKey, string ImageUrl);

/// <summary>
/// Teachers' own backgrounds. The file goes straight from the browser to a private incoming area in R2 through a
/// presigned URL; on completion the server checks and re-encodes it (see <see cref="ImageProcessor"/>), stores the
/// result under a generated name and deletes the original. The teacher's file name is never used or stored.
/// </summary>
public static class UploadEndpoints
{
    public static readonly TimeSpan UploadUrlLifetime = TimeSpan.FromMinutes(10);

    public static IEndpointRouteBuilder MapUploadEndpoints(this IEndpointRouteBuilder app)
    {
        var uploads = app.MapGroup($"{TeacherEndpoints.RoutePrefix}/uploads")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimiting.UploadPolicy);
        uploads.MapPost(string.Empty, StartUpload);
        uploads.MapPost("/{uploadId:guid}/complete", CompleteUploadAsync);
        return app;
    }

    /// <summary>Uploads of one teacher go to their own incoming folder, so nobody can complete another's upload.</summary>
    public static string IncomingKey(Guid teacherId, Guid uploadId) => $"incoming/{teacherId:N}/{uploadId:N}";

    private static IResult StartUpload(StartUploadRequest request, ClaimsPrincipal user, IImageStorage storage)
    {
        if (!storage.IsConfigured)
        {
            return Error(ApiErrors.UploadsUnavailable);
        }

        if (!ImageProcessor.IsAllowedContentType(request.ContentType))
        {
            return Error(ApiErrors.ImageTypeNotAllowed);
        }

        if (request.Size is <= 0 or > ImageProcessor.MaxUploadBytes)
        {
            return Error(ApiErrors.ImageTooLarge);
        }

        var uploadId = Guid.NewGuid();
        var url = storage.CreateUploadUrl(
            IncomingKey(user.GetTeacherId(), uploadId), request.ContentType!.ToLowerInvariant(), UploadUrlLifetime);
        return TypedResults.Ok(new StartUploadResponse(uploadId, url));
    }

    private static async Task<IResult> CompleteUploadAsync(
        Guid uploadId,
        CompleteUploadRequest request,
        ClaimsPrincipal user,
        IImageStorage storage,
        ImageUrls imageUrls,
        CancellationToken cancellationToken)
    {
        if (!storage.IsConfigured)
        {
            return Error(ApiErrors.UploadsUnavailable);
        }

        if (!ImageProcessor.IsAllowedContentType(request.ContentType))
        {
            return Error(ApiErrors.ImageTypeNotAllowed);
        }

        var incomingKey = IncomingKey(user.GetTeacherId(), uploadId);
        var original = await storage.ReadAsync(incomingKey, ImageProcessor.MaxUploadBytes, cancellationToken);
        if (original.Outcome == ReadOutcome.NotFound)
        {
            return TypedResults.Json(new ApiError(ApiErrors.UploadNotFound), statusCode: StatusCodes.Status404NotFound);
        }

        try
        {
            if (original.Outcome == ReadOutcome.TooLarge)
            {
                return Error(ApiErrors.ImageTooLarge);
            }

            var processed = await ImageProcessor.ProcessAsync(
                original.Content!, request.ContentType!.ToLowerInvariant(), cancellationToken);
            if (processed.Problem is { } problem)
            {
                return Error(problem switch
                {
                    ImageProblem.TypeNotAllowed => ApiErrors.ImageTypeNotAllowed,
                    ImageProblem.TooLarge => ApiErrors.ImageTooLarge,
                    _ => ApiErrors.ImageInvalid,
                });
            }

            var key = ImageKeys.NewUploadKey();
            await storage.WriteAsync(key, processed.Content!, ProcessedImage.ContentType, cancellationToken);
            return TypedResults.Ok(new CompleteUploadResponse(key, imageUrls.For(key)!));
        }
        finally
        {
            // The original may hold metadata such as a GPS position; it is never kept.
            await storage.DeleteAsync(incomingKey, CancellationToken.None);
        }
    }

    private static IResult Error(string code) =>
        TypedResults.Json(new ApiError(code), statusCode: StatusCodes.Status400BadRequest);
}
