using Eduscape.Domain.Common;

namespace Eduscape.Domain.Sessions;

/// <summary>
/// A student's device in a session. Holds only a nickname and a random device token – nothing else
/// about students may be stored. Deleted shortly after the session closes.
/// </summary>
public sealed class Participant
{
    internal Participant(Guid teamId, string nickname, string deviceToken, DateTimeOffset joinedAt)
    {
        TeamId = teamId;
        Nickname = Guard.Required(nickname, TextLimits.Nickname);
        DeviceToken = Guard.Required(deviceToken, TextLimits.DeviceToken);
        JoinedAt = joinedAt;
    }

    // For EF Core
    private Participant()
    {
        Nickname = null!;
        DeviceToken = null!;
    }

    public Guid Id { get; private set; } = Guid.CreateVersion7();

    public Guid TeamId { get; internal set; }

    /// <summary>First name or initials.</summary>
    public string Nickname { get; private set; }

    /// <summary>Random token stored in the browser, used to reconnect to the same team.</summary>
    public string DeviceToken { get; private set; }

    public DateTimeOffset JoinedAt { get; private set; }
}
