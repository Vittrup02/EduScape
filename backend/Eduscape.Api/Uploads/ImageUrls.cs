using Eduscape.Domain.Rooms;
using Eduscape.Infrastructure.Storage;

namespace Eduscape.Api.Uploads;

/// <summary>Short-lived links to uploaded images in the private bucket. Built-in images need none.</summary>
public sealed class ImageUrls(IImageStorage storage)
{
    /// <summary>Long enough for a school day's session; a reconnect fetches fresh links.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(12);

    public string? For(string key) =>
        storage.IsConfigured && ImageKeys.IsUpload(key) ? storage.CreateReadUrl(key, Lifetime) : null;
}
