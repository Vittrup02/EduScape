using Eduscape.Domain.Common;
using Eduscape.Domain.Rooms;
using Eduscape.Domain.Sessions;

namespace Eduscape.Tests.Domain;

public class GameplayTests
{
    private static readonly Position Center = new(50, 50);
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);

    // Scene 1: code lock A, decimal lock B (after A), door to scene 2 (after B).
    // Scene 2: multiple-choice lock C and the exit door.
    private readonly EscapeRoom _room = new("Demo", Subject.Mathematics, GradeLevel.Grade7);
    private readonly Lock _lockA;
    private readonly Lock _lockB;
    private readonly Door _door;
    private readonly Lock _lockC;
    private readonly ExitDoor _exit;
    private readonly Team _team;

    public GameplayTests()
    {
        var scene1 = _room.AddScene("Klasselokalet", "builtin:classroom");
        var scene2 = _room.AddScene("Biblioteket", "builtin:library");

        _lockA = scene1.AddObject(new Lock(Center, "builtin:padlock", null, "3 · 4 + 5?", AnswerType.Code, "0017", hint: "Gang først."));
        _lockB = scene1.AddObject(new Lock(Center, "builtin:code-lock", null, "10 / 3?", AnswerType.Text, "3,33", tolerance: 0.01m));
        _lockB.SetUnlockedBy(_lockA);
        _door = scene1.AddObject(new Door(Center, "builtin:door", null, scene2.Id));
        _door.SetUnlockedBy(_lockB);

        _lockC = scene2.AddObject(new Lock(
            Center, "builtin:padlock", null, "Primtal?", AnswerType.MultipleChoice, "17", options: ["9", "17"]));
        _exit = scene2.AddObject(new ExitDoor(Center, "builtin:exit-door", null));

        _team = new Session(Guid.NewGuid(), Guid.NewGuid(), "123456", SessionMode.Team, Now, TimeSpan.FromHours(12))
            .AddTeam(TeamColor.Red);
    }

    private AnswerResult Answer(Lock @lock, string? answer) => Gameplay.SubmitAnswer(_room, _team, @lock.Id, answer, Now);

    private void SolveUpToDoor()
    {
        Answer(_lockA, "0017");
        Answer(_lockB, "3.33");
    }

    [Fact]
    public void CorrectAnswer_SolvesTheLockForTheTeam()
    {
        var result = Answer(_lockA, " 0017 ");

        Assert.Equal(AnswerOutcome.Correct, result.Outcome);
        Assert.True(_team.IsLockSolved(_lockA.Id));
    }

    [Theory]
    [InlineData("17")] // Codes keep their leading zeros.
    [InlineData("0018")]
    public void WrongAnswer_IsCountedForTheTeam(string answer)
    {
        var result = Answer(_lockA, answer);

        Assert.Equal(AnswerOutcome.Wrong, result.Outcome);
        Assert.False(_team.IsLockSolved(_lockA.Id));
        Assert.Equal(1, _team.WrongAttempts(_lockA.Id));
    }

    [Theory]
    [InlineData("3,33")]
    [InlineData("3.33")]
    [InlineData("3,335")]
    [InlineData("3.34")]
    public void NumericAnswer_IsAcceptedWithinTheTolerance(string answer)
    {
        Answer(_lockA, "0017");

        Assert.Equal(AnswerOutcome.Correct, Answer(_lockB, answer).Outcome);
    }

    [Fact]
    public void NumericAnswer_OutsideTheTolerance_IsWrong()
    {
        Answer(_lockA, "0017");

        Assert.Equal(AnswerOutcome.Wrong, Answer(_lockB, "3,35").Outcome);
    }

    [Fact]
    public void MultipleChoice_AcceptsOnlyTheCorrectOption()
    {
        SolveUpToDoor();
        Gameplay.OpenDoor(_room, _team, _door.Id, Now);

        Assert.Equal(AnswerOutcome.Wrong, Answer(_lockC, "9").Outcome);
        Assert.Equal(AnswerOutcome.Correct, Answer(_lockC, "17").Outcome);
    }

    [Fact]
    public void ThirdWrongAnswer_RevealsTheHint_Once()
    {
        Assert.False(Answer(_lockA, "1").HintRevealedNow);
        Assert.False(Answer(_lockA, "2").HintRevealedNow);
        Assert.True(Answer(_lockA, "3").HintRevealedNow);
        Assert.False(Answer(_lockA, "4").HintRevealedNow);

        Assert.True(_team.IsHintVisible(_lockA.Id));
    }

    [Fact]
    public void LockWithoutHint_NeverRevealsOne()
    {
        Answer(_lockA, "0017");

        var results = Enumerable.Range(0, 3).Select(i => Answer(_lockB, $"{i}")).ToList();

        Assert.All(results, r => Assert.False(r.HintRevealedNow));
    }

    [Fact]
    public void AnswerToSolvedLock_IsAlreadySolved_AndChangesNothing()
    {
        Answer(_lockA, "0017");
        var solvedAt = _team.LockProgress.Single().SolvedAt;

        Assert.Equal(AnswerOutcome.AlreadySolved, Gameplay.SubmitAnswer(_room, _team, _lockA.Id, "0017", Now.AddMinutes(1)).Outcome);
        Assert.Equal(AnswerOutcome.AlreadySolved, Answer(_lockA, "wrong").Outcome);

        Assert.Equal(solvedAt, _team.LockProgress.Single().SolvedAt);
        Assert.Equal(0, _team.WrongAttempts(_lockA.Id));
    }

    [Theory]
    [InlineData(null, DomainErrors.ValueRequired)]
    [InlineData("  ", DomainErrors.ValueRequired)]
    public void BlankAnswer_IsRejected_WithoutCounting(string? answer, string code)
    {
        var error = Assert.Throws<DomainException>(() => Answer(_lockA, answer));

        Assert.Equal(code, error.Code);
        Assert.Equal(0, _team.WrongAttempts(_lockA.Id));
    }

    [Fact]
    public void TooLongAnswer_IsRejected()
    {
        var error = Assert.Throws<DomainException>(() => Answer(_lockA, new string('1', TextLimits.Answer + 1)));

        Assert.Equal(DomainErrors.ValueTooLong, error.Code);
    }

    [Fact]
    public void LockHiddenByUnlockedBy_CannotBeAnswered()
    {
        var error = Assert.Throws<DomainException>(() => Answer(_lockB, "3,33"));

        Assert.Equal(DomainErrors.ObjectNotAvailable, error.Code);
        Assert.False(_team.IsLockSolved(_lockB.Id));
    }

    [Fact]
    public void LockInSceneBehindClosedDoor_CannotBeAnswered()
    {
        SolveUpToDoor();

        var error = Assert.Throws<DomainException>(() => Answer(_lockC, "17"));

        Assert.Equal(DomainErrors.ObjectNotAvailable, error.Code);
    }

    [Fact]
    public void UnknownObject_IsNotFound()
    {
        var error = Assert.Throws<DomainException>(() => Gameplay.SubmitAnswer(_room, _team, _door.Id, "1", Now));

        Assert.Equal(DomainErrors.ObjectNotFound, error.Code);
    }

    [Fact]
    public void OpenDoor_RequiresUnlockedBy_AndIsIdempotent()
    {
        Assert.Equal(
            DomainErrors.ObjectNotAvailable,
            Assert.Throws<DomainException>(() => Gameplay.OpenDoor(_room, _team, _door.Id, Now)).Code);

        SolveUpToDoor();

        Assert.True(Gameplay.OpenDoor(_room, _team, _door.Id, Now));
        Assert.False(Gameplay.OpenDoor(_room, _team, _door.Id, Now));
        Assert.Equal([_door.Id], _team.OpenedDoorIds);
    }

    [Fact]
    public void ExitDoor_StaysLockedUntilEveryLockIsSolved()
    {
        SolveUpToDoor();
        Gameplay.OpenDoor(_room, _team, _door.Id, Now);

        var error = Assert.Throws<DomainException>(() => Gameplay.OpenExitDoor(_room, _team, _exit.Id, Now));
        Assert.Equal(DomainErrors.ExitDoorLocked, error.Code);
        Assert.Null(_team.EscapedAt);

        Answer(_lockC, "17");

        Assert.True(Gameplay.OpenExitDoor(_room, _team, _exit.Id, Now));
        Assert.False(Gameplay.OpenExitDoor(_room, _team, _exit.Id, Now.AddMinutes(1)));
        Assert.Equal(Now, _team.EscapedAt);
    }
}
