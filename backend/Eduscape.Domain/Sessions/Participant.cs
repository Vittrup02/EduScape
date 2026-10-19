using Eduscape.Domain.Common;

namespace Eduscape.Domain.Sessions;

/// <summary>
/// A student's device in a session. Holds only an optional nickname and the hash of a random device token –
/// nothing else about students may be stored. Deleted shortly after the session closes.
/// </summary>
public sealed class Participant
{
    internal Participant(Guid teamId, string? nickname, string deviceTokenHash, DateTimeOffset joinedAt)
    {
        TeamId = teamId;
        Nickname = Guard.Optional(nickname, TextLimits.Nickname);
        DeviceTokenHash = Guard.Required(deviceTokenHash, TextLimits.DeviceToken);
        JoinedAt = joinedAt;
    }

    // For EF Core
    private Participant() => DeviceTokenHash = null!;

    public Guid Id { get; private set; } = Guid.CreateVersion7();

    public Guid TeamId { get; internal set; }

    /// <summary>First name or initials; optional.</summary>
    public string? Nickname { get; private set; }

    /// <summary>
    /// Hash of the random token kept in the browser to reconnect to the same team (see <see cref="DeviceToken"/>).
    /// </summary>
    public string DeviceTokenHash { get; private set; }

    public DateTimeOffset JoinedAt { get; private set; }
}
