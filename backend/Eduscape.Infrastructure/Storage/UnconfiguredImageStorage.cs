namespace Eduscape.Infrastructure.Storage;

/// <summary>Used when no R2 bucket is configured, e.g. locally: uploads are off and only built-in images are used.</summary>
internal sealed class UnconfiguredImageStorage : IImageStorage
{
    public bool IsConfigured => false;

    public string CreateUploadUrl(string key, string contentType, TimeSpan expiresIn) => throw NotConfigured();

    public string CreateReadUrl(string key, TimeSpan expiresIn) => throw NotConfigured();

    public Task<ReadResult> ReadAsync(string key, long maxBytes, CancellationToken cancellationToken) => throw NotConfigured();

    public Task WriteAsync(string key, byte[] content, string contentType, CancellationToken cancellationToken) =>
        throw NotConfigured();

    public Task DeleteAsync(string key, CancellationToken cancellationToken) => throw NotConfigured();

    private static InvalidOperationException NotConfigured() =>
        new("Image storage is not configured; check IImageStorage.IsConfigured first.");
}
