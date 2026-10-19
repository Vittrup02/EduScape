using System.Text.RegularExpressions;

namespace Eduscape.Domain.Rooms;

/// <summary>
/// The image keys a room may refer to: a built-in asset ("builtin:classroom", defined in the frontend's asset
/// registry) or an image the server has processed and stored in R2 under a generated name.
/// </summary>
public static partial class ImageKeys
{
    public const string BuiltInPrefix = "builtin:";

    public static bool IsBuiltIn(string key) => BuiltInPattern().IsMatch(key);

    public static bool IsUpload(string key) => UploadPattern().IsMatch(key);

    public static bool IsValid(string? key) => key is not null && (IsBuiltIn(key) || IsUpload(key));

    /// <summary>A new random key for a processed upload; never derived from the teacher's file name.</summary>
    public static string NewUploadKey() => $"uploads/{Guid.NewGuid():N}.webp";

    [GeneratedRegex("^builtin:[a-z0-9-]{1,64}$")]
    private static partial Regex BuiltInPattern();

    [GeneratedRegex("^uploads/[0-9a-f]{32}\\.webp$")]
    private static partial Regex UploadPattern();
}
