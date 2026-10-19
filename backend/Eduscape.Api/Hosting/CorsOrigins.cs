namespace Eduscape.Api.Hosting;

/// <summary>
/// Reads the allowed frontend origins from <c>Cors:AllowedOrigins</c>. Accepts both an array
/// (appsettings, or <c>Cors__AllowedOrigins__0</c> env vars) and a single comma-separated value
/// (<c>Cors__AllowedOrigins=https://a,https://b</c>), which is easier to set on Render.
/// A single value wins over an array, since it can only come from a deliberate override.
/// </summary>
public static class CorsOrigins
{
    public const string SectionName = "Cors:AllowedOrigins";

    public static string[] FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);
        IEnumerable<string?> values = string.IsNullOrWhiteSpace(section.Value)
            ? section.GetChildren().Select(child => child.Value)
            : section.Value.Split(',');

        // Browsers send origins without a trailing slash, so "https://x.pages.dev/" would never match.
        return values
            .Select(value => value?.Trim().TrimEnd('/'))
            .Where(value => !string.IsNullOrEmpty(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
