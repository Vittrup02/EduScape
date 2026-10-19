using Eduscape.Domain.Sessions;

namespace Eduscape.Tests.Domain;

public class TeamTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);

    private static Team NewTeam() =>
        new Session(Guid.NewGuid(), Guid.NewGuid(), "123456", SessionMode.Team, Now, TimeSpan.FromHours(12)).AddTeam(TeamColor.Red);

    [Fact]
    public void TrySolveLock_IsIdempotent()
    {
        var team = NewTeam();
        var lockId = Guid.NewGuid();

        Assert.True(team.TrySolveLock(lockId, Now));
        Assert.False(team.TrySolveLock(lockId, Now.AddSeconds(1)));

        var progress = Assert.Single(team.LockProgress);
        Assert.Equal(Now, progress.SolvedAt);
        Assert.Equal([lockId], team.SolvedLockIds);
    }

    [Fact]
    public void Hint_BecomesVisibleAfterThreeWrongAttempts()
    {
        var team = NewTeam();
        var lockId = Guid.NewGuid();

        team.RegisterWrongAttempt(lockId);
        team.RegisterWrongAttempt(lockId);
        Assert.False(team.IsHintVisible(lockId));

        team.RegisterWrongAttempt(lockId);
        Assert.True(team.IsHintVisible(lockId));
    }

    [Fact]
    public void WrongAttempts_AreCountedPerLock()
    {
        var team = NewTeam();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        for (var i = 0; i < 3; i++)
        {
            team.RegisterWrongAttempt(first);
        }

        Assert.True(team.IsHintVisible(first));
        Assert.False(team.IsHintVisible(second));
    }

    [Fact]
    public void WrongAttempt_AfterSolving_IsIgnored()
    {
        var team = NewTeam();
        var lockId = Guid.NewGuid();
        team.TrySolveLock(lockId, Now);

        team.RegisterWrongAttempt(lockId);

        Assert.Equal(0, team.WrongAttempts(lockId));
    }

    [Fact]
    public void OpenDoor_IsIdempotent()
    {
        var team = NewTeam();
        var doorId = Guid.NewGuid();

        Assert.True(team.OpenDoor(doorId, Now));
        Assert.False(team.OpenDoor(doorId, Now));

        Assert.Single(team.OpenedDoors);
    }

    [Fact]
    public void MarkEscaped_KeepsFirstTime()
    {
        var team = NewTeam();

        team.MarkEscaped(Now);
        team.MarkEscaped(Now.AddMinutes(5));

        Assert.Equal(Now, team.EscapedAt);
    }

    [Fact]
    public void MoveParticipantTo_ChangesTeam()
    {
        var session = new Session(Guid.NewGuid(), Guid.NewGuid(), "123456", SessionMode.Team, Now, TimeSpan.FromHours(12));
        var red = session.AddTeam(TeamColor.Red);
        var blue = session.AddTeam(TeamColor.Blue);
        var participant = red.AddParticipant("Ida", "token-1", Now);

        red.MoveParticipantTo(participant, blue);

        Assert.Empty(red.Participants);
        Assert.Same(participant, Assert.Single(blue.Participants));
        Assert.Equal(blue.Id, participant.TeamId);
    }
}
