using System.Globalization;

namespace Eduscape.Api.Hosting;

/// <summary>
/// Builds the URL Kestrel listens on from the <c>PORT</c> environment variable that Render
/// (and the Docker image) provides. Without <c>PORT</c> the normal ASP.NET Core defaults apply.
/// </summary>
public static class ListenUrl
{
    public static string? FromPort(string? port)
    {
        if (string.IsNullOrWhiteSpace(port))
        {
            return null;
        }

        if (!int.TryParse(port, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            || number is < 1 or > 65535)
        {
            // Fail fast at startup rather than listening somewhere the platform doesn't route to.
            throw new InvalidOperationException($"PORT must be a number between 1 and 65535, but was '{port}'.");
        }

        // 0.0.0.0 so the app is reachable from outside the container.
        return $"http://0.0.0.0:{number}";
    }
}
