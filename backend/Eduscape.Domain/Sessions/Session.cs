using Eduscape.Domain.Common;

namespace Eduscape.Domain.Sessions;

/// <summary>One run of an escape room with a class, joined by students via a six-digit PIN.</summary>
public sealed class Session
{
    public const int PinLength = 6;

    private readonly List<Team> _teams = [];

    public Session(Guid escapeRoomId, Guid classId, string pin, SessionMode mode, DateTimeOffset startedAt)
    {
        if (pin.Length != PinLength || !pin.All(char.IsAsciiDigit))
        {
            throw new DomainException(DomainErrors.SessionPinInvalid);
        }

        EscapeRoomId = escapeRoomId;
        ClassId = classId;
        Pin = pin;
        Mode = mode;
        StartedAt = startedAt;
        Status = SessionStatus.Open;
    }

    // For EF Core
    private Session() => Pin = null!;

    public Guid Id { get; private set; } = Guid.CreateVersion7();

    public Guid EscapeRoomId { get; private set; }

    public Guid ClassId { get; private set; }

    public string Pin { get; private set; }

    public SessionMode Mode { get; private set; }

    public SessionStatus Status { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public IReadOnlyList<Team> Teams => _teams;

    public Team AddTeam(TeamColor color)
    {
        EnsureNotClosed();
        var team = new Team(Id, color);
        _teams.Add(team);
        return team;
    }

    /// <summary>Stops new students from joining.</summary>
    public void Lock()
    {
        EnsureNotClosed();
        Status = SessionStatus.Locked;
    }

    public void Unlock()
    {
        EnsureNotClosed();
        Status = SessionStatus.Open;
    }

    public void Close(DateTimeOffset now)
    {
        if (Status == SessionStatus.Closed)
        {
            return;
        }

        Status = SessionStatus.Closed;
        ClosedAt = now;
    }

    private void EnsureNotClosed()
    {
        if (Status == SessionStatus.Closed)
        {
            throw new DomainException(DomainErrors.SessionClosed);
        }
    }
}
