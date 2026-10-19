using Eduscape.Domain.Common;
using Eduscape.Domain.Sessions;

namespace Eduscape.Tests.Domain;

public class SessionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(12);

    [Theory]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12a456")]
    [InlineData("١٢٣٤٥٦")] // non-ASCII digits
    public void Constructor_RejectsInvalidPin(string pin)
    {
        var ex = Assert.Throws<DomainException>(() =>
            new Session(Guid.NewGuid(), Guid.NewGuid(), pin, SessionMode.Team, Now, Lifetime));

        Assert.Equal(DomainErrors.SessionPinInvalid, ex.Code);
    }

    [Fact]
    public void Constructor_RejectsNonPositiveLifetime() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Session(Guid.NewGuid(), Guid.NewGuid(), "123456", SessionMode.Team, Now, TimeSpan.Zero));

    [Fact]
    public void Close_IsTerminal()
    {
        var session = new Session(Guid.NewGuid(), Guid.NewGuid(), "042042", SessionMode.Team, Now, Lifetime);

        session.Lock();
        session.Close(Now.AddHours(1));
        session.Close(Now.AddHours(2));

        Assert.Equal(SessionStatus.Closed, session.Status);
        Assert.Equal(Now.AddHours(1), session.ClosedAt);
        Assert.Equal(DomainErrors.SessionClosed, Assert.Throws<DomainException>(session.Unlock).Code);
        Assert.Equal(DomainErrors.SessionClosed, Assert.Throws<DomainException>(() => session.AddTeam(TeamColor.Red)).Code);
    }

    [Fact]
    public void Participant_NicknameIsTrimmedAndLimited()
    {
        var team = new Session(Guid.NewGuid(), Guid.NewGuid(), "123456", SessionMode.Individual, Now, Lifetime).AddTeam(TeamColor.Blue);

        Assert.Equal("AB", team.AddParticipant("  AB ", "token", Now).Nickname);
        Assert.Equal(
            DomainErrors.ValueTooLong,
            Assert.Throws<DomainException>(() => team.AddParticipant(new string('x', 21), "token-2", Now)).Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Participant_NicknameIsOptional(string? nickname)
    {
        var session = StartTeamSession(2);

        var participant = session.Join(session.Teams[0].Id, nickname, "hash", Now);

        Assert.Null(participant.Nickname);
    }

    [Fact]
    public void Start_InTeamMode_CreatesOneTeamPerColour()
    {
        var session = StartTeamSession(3);

        Assert.Equal([TeamColor.Red, TeamColor.Blue, TeamColor.Green], session.Teams.Select(t => t.Color));
        Assert.Equal(Now + Lifetime, session.ExpiresAt);
        Assert.Equal(SessionStatus.Open, session.Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(9)]
    public void Start_InTeamMode_RejectsInvalidTeamCount(int teamCount)
    {
        var ex = Assert.Throws<DomainException>(() =>
            Session.Start(Guid.NewGuid(), Guid.NewGuid(), "123456", SessionMode.Team, teamCount, Now, Lifetime));

        Assert.Equal(DomainErrors.TeamCountInvalid, ex.Code);
    }

    [Fact]
    public void Start_InTeamMode_AllowsOneTeamPerColour()
    {
        var session = StartTeamSession(Session.MaxTeamCount);

        Assert.Equal(Enum.GetValues<TeamColor>(), session.Teams.Select(t => t.Color));
    }

    [Fact]
    public void Start_InIndividualMode_CreatesNoTeamsAndIgnoresTeamCount()
    {
        var session = StartIndividualSession();

        Assert.Empty(session.Teams);
    }

    [Fact]
    public void Join_InTeamMode_AddsStudentToTheChosenTeam()
    {
        var session = StartTeamSession(3);
        var blue = session.Teams[1];

        var participant = session.Join(blue.Id, "Ida", "hash-1", Now);

        Assert.Equal(blue.Id, participant.TeamId);
        Assert.Same(participant, Assert.Single(blue.Participants));
        Assert.Same(blue, session.FindTeamOf(participant.Id));
        Assert.Equal(3, session.Teams.Count);
    }

    [Fact]
    public void Join_InTeamMode_WithUnknownTeam_Throws()
    {
        var session = StartTeamSession(2);

        var ex = Assert.Throws<DomainException>(() => session.Join(Guid.NewGuid(), "Ida", "hash", Now));
        Assert.Equal(DomainErrors.TeamNotFound, ex.Code);

        ex = Assert.Throws<DomainException>(() => session.Join(null, "Ida", "hash", Now));
        Assert.Equal(DomainErrors.TeamNotFound, ex.Code);
    }

    [Fact]
    public void Join_InIndividualMode_CreatesATeamPerStudent_IgnoringTheTeamId()
    {
        var session = StartIndividualSession();

        var ida = session.Join(Guid.NewGuid(), "Ida", "hash-1", Now);
        var bo = session.Join(null, "Bo", "hash-2", Now);

        Assert.Equal(2, session.Teams.Count);
        Assert.NotEqual(ida.TeamId, bo.TeamId);
        Assert.All(session.Teams, team => Assert.Single(team.Participants));
        Assert.Equal([TeamColor.Red, TeamColor.Blue], session.Teams.Select(t => t.Color));
    }

    [Fact]
    public void Join_InIndividualMode_CyclesColoursBeyondTheLastOne()
    {
        var session = StartIndividualSession();

        for (var i = 0; i <= Session.MaxTeamCount; i++)
        {
            session.Join(null, null, $"hash-{i}", Now);
        }

        Assert.Equal(TeamColor.Red, session.Teams[^1].Color);
    }

    [Fact]
    public void Join_WhenLocked_Throws()
    {
        var session = StartTeamSession(2);
        session.Lock();

        var ex = Assert.Throws<DomainException>(() => session.Join(session.Teams[0].Id, "Ida", "hash", Now));

        Assert.Equal(DomainErrors.SessionLocked, ex.Code);
    }

    [Fact]
    public void Join_AfterUnlock_Works()
    {
        var session = StartTeamSession(2);
        session.Lock();
        session.Unlock();

        session.Join(session.Teams[0].Id, "Ida", "hash", Now);

        Assert.Single(session.Teams[0].Participants);
    }

    [Fact]
    public void Join_WhenClosed_Throws()
    {
        var session = StartTeamSession(2);
        session.Close(Now);

        var ex = Assert.Throws<DomainException>(() => session.Join(session.Teams[0].Id, "Ida", "hash", Now));

        Assert.Equal(DomainErrors.SessionClosed, ex.Code);
    }

    [Fact]
    public void Join_WhenExpired_Throws()
    {
        var session = StartTeamSession(2);

        var ex = Assert.Throws<DomainException>(() => session.Join(session.Teams[0].Id, "Ida", "hash", Now + Lifetime));

        Assert.Equal(DomainErrors.PinExpired, ex.Code);
    }

    [Fact]
    public void IsExpired_StartsAtExpiresAt()
    {
        var session = StartTeamSession(2);
        var justBefore = Now + Lifetime - TimeSpan.FromTicks(1);

        Assert.False(session.IsExpired(justBefore));
        Assert.True(session.IsActive(justBefore));
        Assert.True(session.IsExpired(Now + Lifetime));
        Assert.False(session.IsActive(Now + Lifetime));
    }

    [Fact]
    public void CloseIfExpired_ClosesAsOfTheExpiryTime()
    {
        var session = StartTeamSession(2);

        Assert.False(session.CloseIfExpired(Now.AddHours(1)));
        Assert.Equal(SessionStatus.Open, session.Status);

        Assert.True(session.CloseIfExpired(Now.AddDays(2)));
        Assert.Equal(SessionStatus.Closed, session.Status);
        Assert.Equal(session.ExpiresAt, session.ClosedAt);

        Assert.False(session.CloseIfExpired(Now.AddDays(3)));
    }

    private static Session StartTeamSession(int teamCount) =>
        Session.Start(Guid.NewGuid(), Guid.NewGuid(), "123456", SessionMode.Team, teamCount, Now, Lifetime);

    private static Session StartIndividualSession() =>
        Session.Start(Guid.NewGuid(), Guid.NewGuid(), "123456", SessionMode.Individual, 0, Now, Lifetime);
}
