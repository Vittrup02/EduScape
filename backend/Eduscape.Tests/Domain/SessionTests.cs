using Eduscape.Domain.Common;
using Eduscape.Domain.Sessions;

namespace Eduscape.Tests.Domain;

public class SessionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12a456")]
    [InlineData("١٢٣٤٥٦")] // non-ASCII digits
    public void Constructor_RejectsInvalidPin(string pin)
    {
        var ex = Assert.Throws<DomainException>(() =>
            new Session(Guid.NewGuid(), Guid.NewGuid(), pin, SessionMode.Team, Now));

        Assert.Equal(DomainErrors.SessionPinInvalid, ex.Code);
    }

    [Fact]
    public void Close_IsTerminal()
    {
        var session = new Session(Guid.NewGuid(), Guid.NewGuid(), "042042", SessionMode.Team, Now);

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
        var team = new Session(Guid.NewGuid(), Guid.NewGuid(), "123456", SessionMode.Individual, Now).AddTeam(TeamColor.Blue);

        Assert.Equal("AB", team.AddParticipant("  AB ", "token", Now).Nickname);
        Assert.Equal(
            DomainErrors.ValueTooLong,
            Assert.Throws<DomainException>(() => team.AddParticipant(new string('x', 21), "token-2", Now)).Code);
    }
}
