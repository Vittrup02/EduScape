using Eduscape.Domain.Common;
using Eduscape.Domain.Sessions;

namespace Eduscape.Tests.Domain;

/// <summary>The domain rules behind the teacher's live dashboard: who is stuck where, moving students, cleanup.</summary>
public class TeacherDashboardTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);

    private static Session NewSession(SessionMode mode = SessionMode.Team) =>
        Session.Start(Guid.NewGuid(), Guid.NewGuid(), "123456", mode, teamCount: 2, Now, TimeSpan.FromHours(12));

    [Fact]
    public void WrongAttempts_RecordFirstAndLastAttempt()
    {
        var team = NewSession().Teams[0];
        var lockId = Guid.NewGuid();

        team.RegisterWrongAttempt(lockId, Now);
        team.RegisterWrongAttempt(lockId, Now.AddMinutes(3));

        var progress = Assert.Single(team.LockProgress);
        Assert.Equal(Now, progress.FirstAttemptAt);
        Assert.Equal(Now.AddMinutes(3), progress.LastAttemptAt);
    }

    [Fact]
    public void FindCurrentLock_IsNullBeforeAnyAttempt()
    {
        var team = NewSession().Teams[0];
        team.TrySolveLock(Guid.NewGuid(), Now);

        Assert.Null(team.FindCurrentLock());
    }

    [Fact]
    public void FindCurrentLock_IsTheUnsolvedLockTriedLast()
    {
        var team = NewSession().Teams[0];
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        team.RegisterWrongAttempt(first, Now);
        team.RegisterWrongAttempt(second, Now.AddMinutes(1));
        team.RegisterWrongAttempt(second, Now.AddMinutes(2));

        Assert.Equal(new CurrentLock(second, Now.AddMinutes(1), WrongAttempts: 2), team.FindCurrentLock());
    }

    [Fact]
    public void FindCurrentLock_SkipsSolvedLocks()
    {
        var team = NewSession().Teams[0];
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        team.RegisterWrongAttempt(first, Now);
        team.RegisterWrongAttempt(second, Now.AddMinutes(1));
        team.TrySolveLock(second, Now.AddMinutes(2));

        Assert.Equal(first, team.FindCurrentLock()?.LockId);
    }

    [Fact]
    public void FindCurrentLock_CountsFromTheTeamsLastProgress_WhenThatCameLater()
    {
        var team = NewSession().Teams[0];
        var stuck = Guid.NewGuid();
        team.RegisterWrongAttempt(stuck, Now);
        team.OpenDoor(Guid.NewGuid(), Now.AddMinutes(4));
        team.RegisterWrongAttempt(stuck, Now.AddMinutes(5));

        Assert.Equal(Now.AddMinutes(4), team.FindCurrentLock()?.StuckSince);
    }

    [Fact]
    public void FindCurrentLock_IsNullOnceTheTeamEscaped()
    {
        var team = NewSession().Teams[0];
        team.RegisterWrongAttempt(Guid.NewGuid(), Now);
        team.MarkEscaped(Now.AddMinutes(1));

        Assert.Null(team.FindCurrentLock());
    }

    [Fact]
    public void EnterScene_KeepsTheArrivalTime_WhenTheSameSceneIsReportedAgain()
    {
        var session = NewSession();
        var participant = session.Join(session.Teams[0].Id, "Ida", "token", Now);
        var sceneId = Guid.NewGuid();

        participant.EnterScene(sceneId, Now.AddMinutes(1));
        participant.EnterScene(sceneId, Now.AddMinutes(2));

        Assert.Equal(sceneId, participant.CurrentSceneId);
        Assert.Equal(Now.AddMinutes(1), participant.SceneEnteredAt);
    }

    [Fact]
    public void MoveParticipant_MovesTheStudentToTheTeam()
    {
        var session = NewSession();
        var participant = session.Join(session.Teams[0].Id, "Ida", "token", Now);

        session.MoveParticipant(participant.Id, session.Teams[1].Id);

        Assert.Empty(session.Teams[0].Participants);
        Assert.Same(participant, Assert.Single(session.Teams[1].Participants));
    }

    [Fact]
    public void MoveParticipant_InIndividualMode_IsNotAllowed()
    {
        var session = NewSession(SessionMode.Individual);
        var ida = session.Join(teamId: null, "Ida", "token-1", Now);
        session.Join(teamId: null, "Bo", "token-2", Now);

        var ex = Assert.Throws<DomainException>(() => session.MoveParticipant(ida.Id, session.Teams[1].Id));

        Assert.Equal(DomainErrors.ParticipantMoveNotAllowed, ex.Code);
    }

    [Fact]
    public void MoveParticipant_RejectsUnknownStudentsAndTeams()
    {
        var session = NewSession();
        var participant = session.Join(session.Teams[0].Id, "Ida", "token", Now);

        Assert.Equal(
            DomainErrors.ParticipantNotFound,
            Assert.Throws<DomainException>(() => session.MoveParticipant(Guid.NewGuid(), session.Teams[1].Id)).Code);
        Assert.Equal(
            DomainErrors.TeamNotFound,
            Assert.Throws<DomainException>(() => session.MoveParticipant(participant.Id, Guid.NewGuid())).Code);
    }

    [Fact]
    public void MoveParticipant_InClosedSession_IsRejected()
    {
        var session = NewSession();
        var participant = session.Join(session.Teams[0].Id, "Ida", "token", Now);
        session.Close(Now);

        var ex = Assert.Throws<DomainException>(() => session.MoveParticipant(participant.Id, session.Teams[1].Id));

        Assert.Equal(DomainErrors.SessionClosed, ex.Code);
    }

    [Fact]
    public void EraseStudentData_KeepsTheResultsAndHowManyPlayed()
    {
        var session = NewSession();
        var red = session.Teams[0];
        session.Join(red.Id, "Ida", "token-1", Now);
        session.Join(red.Id, "Bo", "token-2", Now);
        var lockId = Guid.NewGuid();
        red.TrySolveLock(lockId, Now.AddMinutes(5));
        session.Close(Now.AddMinutes(30));

        session.EraseStudentData();

        Assert.Empty(red.Participants);
        Assert.Equal(2, red.ParticipantCount);
        Assert.Equal(0, session.Teams[1].ParticipantCount);
        Assert.Equal([lockId], red.SolvedLockIds);
    }

    [Fact]
    public void EraseStudentData_BeforeClosing_Throws()
    {
        var session = NewSession();

        Assert.Throws<InvalidOperationException>(session.EraseStudentData);
    }
}
