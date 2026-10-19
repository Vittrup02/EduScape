namespace Eduscape.Api.Sessions;

/// <summary>Settings from the <c>Sessions</c> configuration section.</summary>
public sealed class SessionOptions
{
    public const string SectionName = "Sessions";

    /// <summary>How long a PIN works when the teacher does not close the session. Covers a school day.</summary>
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromHours(12);
}
