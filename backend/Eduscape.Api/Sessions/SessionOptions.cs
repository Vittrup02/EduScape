namespace Eduscape.Api.Sessions;

/// <summary>Settings from the <c>Sessions</c> configuration section.</summary>
public sealed class SessionOptions
{
    public const string SectionName = "Sessions";

    /// <summary>How long a PIN works when the teacher does not close the session. Covers a school day.</summary>
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromHours(12);

    /// <summary>How long a teacher's preview of a room lasts. Its data is then cleaned up like any session's.</summary>
    public TimeSpan PreviewLifetime { get; set; } = TimeSpan.FromHours(2);

    /// <summary>
    /// How long students' nicknames and device tokens are kept after a session closes, so the teacher can still see
    /// the final overview with names right after the lesson.
    /// </summary>
    public TimeSpan StudentDataRetention { get; set; } = TimeSpan.FromHours(1);

    /// <summary>How often the student data cleanup runs.</summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(10);
}
