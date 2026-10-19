using System.Collections.Concurrent;
using Eduscape.Infrastructure.Storage;

namespace Eduscape.Tests.Api;

/// <summary>An in-memory bucket. Tests put files in <see cref="Objects"/> where the browser would PUT them.</summary>
internal sealed class FakeImageStorage : IImageStorage
{
    public ConcurrentDictionary<string, (byte[] Content, string ContentType)> Objects { get; } = new();

    public bool IsConfigured => true;

    public string CreateUploadUrl(string key, string contentType, TimeSpan expiresIn) =>
        $"https://bucket.example/{key}?put&type={Uri.EscapeDataString(contentType)}";

    public string CreateReadUrl(string key, TimeSpan expiresIn) => $"https://bucket.example/{key}?get";

    public Task<ReadResult> ReadAsync(string key, long maxBytes, CancellationToken cancellationToken) =>
        Task.FromResult(Objects.TryGetValue(key, out var stored)
            ? stored.Content.Length > maxBytes ? ReadResult.TooLarge : new ReadResult(ReadOutcome.Found, stored.Content)
            : ReadResult.NotFound);

    public Task WriteAsync(string key, byte[] content, string contentType, CancellationToken cancellationToken)
    {
        Objects[key] = (content, contentType);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        Objects.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}
