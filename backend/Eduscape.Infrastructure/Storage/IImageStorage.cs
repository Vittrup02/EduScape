namespace Eduscape.Infrastructure.Storage;

public enum ReadOutcome
{
    Found,
    NotFound,
    TooLarge,
}

/// <summary>The result of reading an object; <c>Content</c> is set only when it was found within the size limit.</summary>
public sealed record ReadResult(ReadOutcome Outcome, byte[]? Content)
{
    public static readonly ReadResult NotFound = new(ReadOutcome.NotFound, null);
    public static readonly ReadResult TooLarge = new(ReadOutcome.TooLarge, null);
}

/// <summary>
/// Object storage for teachers' images (Cloudflare R2 in production). The bucket is private: browsers upload with a
/// presigned PUT URL and show images through short-lived presigned GET URLs.
/// </summary>
public interface IImageStorage
{
    /// <summary>False when no bucket is configured; uploads are then switched off and nothing else is called.</summary>
    bool IsConfigured { get; }

    /// <summary>A URL the browser can PUT the file to directly. The content type is part of the signature.</summary>
    string CreateUploadUrl(string key, string contentType, TimeSpan expiresIn);

    /// <summary>A URL the browser can show the image from until it expires.</summary>
    string CreateReadUrl(string key, TimeSpan expiresIn);

    /// <summary>Reads an object, without downloading it if it is larger than <paramref name="maxBytes"/>.</summary>
    Task<ReadResult> ReadAsync(string key, long maxBytes, CancellationToken cancellationToken);

    Task WriteAsync(string key, byte[] content, string contentType, CancellationToken cancellationToken);

    Task DeleteAsync(string key, CancellationToken cancellationToken);
}
