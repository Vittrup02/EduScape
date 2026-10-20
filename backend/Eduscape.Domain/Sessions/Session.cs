using Eduscape.Domain.Common;

namespace Eduscape.Domain.Sessions;

/// <summary>One run of an escape room with a class, joined by students via a six-digit PIN.</summary>
public sealed class Session
{
    public const int PinLength = 6;

    /// <summary>A team-mode session has one team per colour at most.</summary>
    public static readonly int MaxTeamCount = Enum.GetValues<TeamColor>().Length;

    public const int MinTeamCount = 2;

    private readonly List<Team> _teams = [];

    public Session(
        Guid escapeRoomId,
        Guid? classId,
        string pin,
        SessionMode mode,
        DateTimeOffset startedAt,
        TimeSpan lifetime)
    {
        if (!IsValidPin(pin))
        {
            throw new DomainException(DomainErrors.SessionPinInvalid);
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lifetime, TimeSpan.Zero);

        EscapeRoomId = escapeRoomId;
        ClassId = classId;
        Pin = pin;
        Mode = mode;
        StartedAt = startedAt;
        ExpiresAt = startedAt + lifetime;
        Status = SessionStatus.Open;
    }

    // For EF Core
    private Session() => Pin = null!;

    public Guid Id { get; private set; } = Guid.CreateVersion7();

    public Guid EscapeRoomId { get; private set; }

    /// <summary>The class the session is run with; null only for a preview.</summary>
    public Guid? ClassId { get; private set; }

    /// <summary>A teacher trying their own room. Hidden from session lists, the dashboard and class history.</summary>
    public bool IsPreview { get; private set; }

    public string Pin { get; private set; }

    public SessionMode Mode { get; private set; }

    public SessionStatus Status { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    /// <summary>When the PIN stops working and the session closes, unless the teacher closes it before.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public IReadOnlyList<Team> Teams => _teams;

    /// <summary>
    /// Starts a session. In team mode the teams are created up front (one colour each) for students to choose from;
    /// in individual mode they are created as students join.
    /// </summary>
    public static Session Start(
        Guid escapeRoomId,
        Guid classId,
        string pin,
        SessionMode mode,
        int teamCount,
        DateTimeOffset now,
        TimeSpan lifetime)
    {
        var session = new Session(escapeRoomId, classId, pin, mode, now, lifetime);
        if (mode == SessionMode.Team)
        {
            if (teamCount < MinTeamCount || teamCount > MaxTeamCount)
            {
                throw new DomainException(DomainErrors.TeamCountInvalid);
            }

            foreach (var color in Enum.GetValues<TeamColor>().Take(teamCount))
            {
                session.AddTeam(color);
            }
        }

        return session;
    }

    /// <summary>
    /// Lets a teacher try a room exactly as students play it: an individual session without a class, which the
    /// teacher joins as its only participant before it is locked against anyone else joining.
    /// </summary>
    public static (Session Session, Participant Participant) StartPreview(
        Guid escapeRoomId, string pin, string deviceTokenHash, DateTimeOffset now, TimeSpan lifetime)
    {
        var session = new Session(escapeRoomId, classId: null, pin, SessionMode.Individual, now, lifetime)
        {
            IsPreview = true,
        };
        var participant = session.Join(teamId: null, nickname: null, deviceTokenHash, now);
        session.Lock();
        return (session, participant);
    }

    /// <summary>Six ASCII digits; leading zeros are allowed.</summary>
    public static bool IsValidPin(string? pin) => pin is { Length: PinLength } && pin.All(char.IsAsciiDigit);

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

    /// <summary>True while the session can be played: not closed and not expired.</summary>
    public bool IsActive(DateTimeOffset now) => Status != SessionStatus.Closed && !IsExpired(now);

    public Team AddTeam(TeamColor color)
    {
        EnsureNotClosed();
        var team = new Team(Id, color);
        _teams.Add(team);
        return team;
    }

    /// <summary>
    /// Adds a student to the session. In team mode the student joins the chosen team; in individual mode the server
    /// creates a team for the student. From there on both modes are the same.
    /// </summary>
    public Participant Join(Guid? teamId, string? nickname, string deviceTokenHash, DateTimeOffset now)
    {
        EnsureJoinable(now);

        var team = Mode == SessionMode.Individual
            ? AddTeam(NextIndividualColor())
            : _teams.Find(t => t.Id == teamId) ?? throw new DomainException(DomainErrors.TeamNotFound);

        return team.AddParticipant(nickname, deviceTokenHash, now);
    }

    /// <summary>Throws the reason a student cannot join right now (closed, expired or locked).</summary>
    public void EnsureJoinable(DateTimeOffset now)
    {
        if (Status == SessionStatus.Closed)
        {
            throw new DomainException(DomainErrors.SessionClosed);
        }

        if (IsExpired(now))
        {
            throw new DomainException(DomainErrors.PinExpired);
        }

        if (Status == SessionStatus.Locked)
        {
            throw new DomainException(DomainErrors.SessionLocked);
        }
    }

    public Team? FindTeamOf(Guid participantId) =>
        _teams.Find(t => t.Participants.Any(p => p.Id == participantId));

    /// <summary>
    /// The teacher moves a student to another team. Only in team mode: in individual mode every team is one student.
    /// The student keeps playing with the new team's progress.
    /// </summary>
    public Participant MoveParticipant(Guid participantId, Guid targetTeamId)
    {
        EnsureNotClosed();
        if (Mode == SessionMode.Individual)
        {
            throw new DomainException(DomainErrors.ParticipantMoveNotAllowed);
        }

        var from = FindTeamOf(participantId) ?? throw new DomainException(DomainErrors.ParticipantNotFound);
        var to = _teams.Find(t => t.Id == targetTeamId) ?? throw new DomainException(DomainErrors.TeamNotFound);
        var participant = from.Participants.Single(p => p.Id == participantId);
        from.MoveParticipantTo(participant, to);
        return participant;
    }

    /// <summary>
    /// Deletes the students' data (nicknames, device tokens) of a closed session; the teams' results stay, without
    /// names, for the teacher's history.
    /// </summary>
    public void EraseStudentData()
    {
        if (Status != SessionStatus.Closed)
        {
            throw new InvalidOperationException("Student data is erased only after the session is closed.");
        }

        foreach (var team in _teams)
        {
            team.EraseParticipants();
        }
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

    /// <summary>Closes the session as of its expiry time if that has passed. Returns true if it was closed now.</summary>
    public bool CloseIfExpired(DateTimeOffset now)
    {
        if (Status == SessionStatus.Closed || !IsExpired(now))
        {
            return false;
        }

        Close(ExpiresAt);
        return true;
    }

    private TeamColor NextIndividualColor()
    {
        var colors = Enum.GetValues<TeamColor>();
        return colors[_teams.Count % colors.Length];
    }

    private void EnsureNotClosed()
    {
        if (Status == SessionStatus.Closed)
        {
            throw new DomainException(DomainErrors.SessionClosed);
        }
    }
}
