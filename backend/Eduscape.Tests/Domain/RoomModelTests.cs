using Eduscape.Domain.Common;
using Eduscape.Domain.Rooms;

namespace Eduscape.Tests.Domain;

public class RoomModelTests
{
    private static readonly Position Center = new(50, 50);

    [Theory]
    [InlineData(-0.1, 50)]
    [InlineData(50, 100.1)]
    [InlineData(double.NaN, 50)]
    public void Position_RejectsValuesOutsidePercentageRange(double x, double y)
    {
        var ex = Assert.Throws<DomainException>(() => new Position(x, y));

        Assert.Equal(DomainErrors.PositionOutOfRange, ex.Code);
    }

    [Fact]
    public void Position_AcceptsEdges()
    {
        var position = new Position(0, 100);

        Assert.Equal(0, position.X);
        Assert.Equal(100, position.Y);
    }

    [Fact]
    public void AddScene_FirstSceneBecomesStartScene()
    {
        var room = new EscapeRoom("Rum", Subject.History, GradeLevel.Grade8);

        var first = room.AddScene("Første", "a.jpg");
        var second = room.AddScene("Anden", "b.jpg");

        Assert.Same(first, room.StartScene);
        Assert.Equal(1, second.SortOrder);

        room.SetStartScene(second.Id);
        Assert.Same(second, room.StartScene);
        Assert.False(first.IsStartScene);
    }

    [Fact]
    public void SetStartScene_UnknownScene_Throws()
    {
        var room = new EscapeRoom("Rum", Subject.History, GradeLevel.Grade8);

        var ex = Assert.Throws<DomainException>(() => room.SetStartScene(Guid.NewGuid()));

        Assert.Equal(DomainErrors.SceneNotFound, ex.Code);
    }

    [Theory]
    [InlineData(AnswerType.Code)]
    [InlineData(AnswerType.MultipleChoice)]
    public void Lock_ToleranceOnlyAllowedForText(AnswerType answerType)
    {
        var ex = Assert.Throws<DomainException>(() =>
            new Lock(Center, "lock.png", null, "?", answerType, "1", tolerance: 0.1m, options: ["1", "2"]));

        Assert.Equal(DomainErrors.LockToleranceInvalid, ex.Code);
    }

    [Fact]
    public void Lock_NegativeToleranceRejected()
    {
        var ex = Assert.Throws<DomainException>(() =>
            new Lock(Center, "lock.png", null, "?", AnswerType.Text, "1", tolerance: -0.01m));

        Assert.Equal(DomainErrors.LockToleranceInvalid, ex.Code);
    }

    [Fact]
    public void Lock_OptionsOnlyAllowedForMultipleChoice()
    {
        var ex = Assert.Throws<DomainException>(() =>
            new Lock(Center, "lock.png", null, "?", AnswerType.Code, "1", options: ["1", "2"]));

        Assert.Equal(DomainErrors.LockOptionsInvalid, ex.Code);
    }

    [Fact]
    public void Lock_MayBeUnfinishedWhileEditing_AndIsTrimmed()
    {
        var draft = new Lock(Center, "lock.png", null, "  ", AnswerType.Code, "", options: null);

        Assert.Equal(string.Empty, draft.Steps[0].Question);
        Assert.Equal(string.Empty, draft.Steps[0].CorrectAnswer);

        draft.SetSteps([new LockStep(" Hvad er 2 + 2? ", AnswerType.Text, " 4 ", hint: "  ")]);
        Assert.Equal("Hvad er 2 + 2?", draft.Steps[0].Question);
        Assert.Equal("4", draft.Steps[0].CorrectAnswer);
        Assert.Null(draft.Steps[0].Hint);
    }

    [Fact]
    public void Lock_TextIsStillLimitedInLength()
    {
        var ex = Assert.Throws<DomainException>(() =>
            new Lock(Center, "lock.png", null, new string('a', TextLimits.Question + 1), AnswerType.Code, "1"));

        Assert.Equal(DomainErrors.ValueTooLong, ex.Code);
    }

    [Fact]
    public void LockStep_SynonymsAndTypos_OnlyForTextAnswers()
    {
        var text = new LockStep("?", AnswerType.Text, "vand", acceptedAnswers: [" H2O ", " "], acceptTypos: true);

        Assert.Equal(["H2O"], text.AcceptedAnswers);
        Assert.True(text.AcceptTypos);
        Assert.Equal(
            DomainErrors.LockStepInvalid,
            Assert.Throws<DomainException>(() => new LockStep("?", AnswerType.Code, "1", acceptTypos: true)).Code);
        Assert.Equal(
            DomainErrors.LockStepInvalid,
            Assert.Throws<DomainException>(() => new LockStep("?", AnswerType.MultipleChoice, "1", acceptedAnswers: ["2"])).Code);
    }

    [Fact]
    public void LockStep_OrderItems_OnlyForOrderSteps_WithUploadedImages()
    {
        var item = new OrderItem(Guid.NewGuid(), " Afvej ", "uploads/0123456789abcdef0123456789abcdef.webp");
        var step = new LockStep("Rækkefølgen?", AnswerType.Order, "ignored", orderItems: [item]);

        Assert.Equal("Afvej", step.OrderItems[0].Text);
        Assert.Equal(string.Empty, step.CorrectAnswer);
        Assert.Equal(
            DomainErrors.LockStepInvalid,
            Assert.Throws<DomainException>(() => new LockStep("?", AnswerType.Text, "1", orderItems: [item])).Code);
        Assert.Equal(
            DomainErrors.ImageKeyInvalid,
            Assert.Throws<DomainException>(() => new OrderItem(Guid.NewGuid(), "Afvej", "https://example.com/a.png")).Code);
    }

    [Fact]
    public void Lock_StepsAreLimited_AndNeedTheirOwnIds()
    {
        var steps = Enumerable.Range(0, Lock.MaxSteps + 1).Select(i => new LockStep($"{i}?", AnswerType.Code, "1")).ToList();
        var @lock = new Lock(Center, "lock.png", null, []);

        Assert.Equal(DomainErrors.ListTooLong, Assert.Throws<DomainException>(() => @lock.SetSteps(steps)).Code);

        var step = new LockStep("?", AnswerType.Code, "1");
        Assert.Equal(DomainErrors.LockStepInvalid, Assert.Throws<DomainException>(() => @lock.SetSteps([step, step])).Code);
    }

    [Fact]
    public void Lock_GraphicChanges_WithoutTouchingItsPuzzle()
    {
        var @lock = new Lock(Center, "builtin:padlock", null, "Hvad er Na?", AnswerType.Text, "Natrium");
        var puzzle = @lock.Steps;

        @lock.SetGraphic("builtin:chest", null);

        Assert.Equal("builtin:chest", @lock.GraphicKey);
        Assert.Same(puzzle, @lock.Steps);
    }

    [Fact]
    public void Room_WaitAfterWrongAnswer_DefaultsTo15Seconds_AndIsLimited()
    {
        var room = new EscapeRoom("Rum", Subject.Chemistry, GradeLevel.Stx2);
        Assert.Equal(15, room.WrongAnswerCooldownSeconds);

        room.SetWrongAnswerCooldown(0);
        Assert.Equal(0, room.WrongAnswerCooldownSeconds);

        Assert.Equal(DomainErrors.CooldownInvalid, Assert.Throws<DomainException>(() => room.SetWrongAnswerCooldown(-1)).Code);
        Assert.Equal(DomainErrors.CooldownInvalid, Assert.Throws<DomainException>(() => room.SetWrongAnswerCooldown(301)).Code);
    }

    [Fact]
    public void Room_ScenesCanBeReordered()
    {
        var room = new EscapeRoom("Rum", Subject.Chemistry, GradeLevel.Stx2);
        var first = room.AddScene("Første", "s1.jpg");
        var second = room.AddScene("Anden", "s2.jpg");

        room.ReorderScenes([second.Id, first.Id]);

        Assert.Equal([second, first], room.Scenes);
        Assert.Equal((0, 1), (second.SortOrder, first.SortOrder));
        Assert.True(first.IsStartScene);
        Assert.Equal(DomainErrors.SceneNotFound, Assert.Throws<DomainException>(() => room.ReorderScenes([first.Id])).Code);
    }

    [Fact]
    public void Letter_MayBeEmptyWhileEditing()
    {
        var letter = new Letter(Center, "letter.png", " ");

        Assert.Equal(string.Empty, letter.Text);
    }

    [Fact]
    public void SetGraphic_ChangesBothKeys()
    {
        var door = new Door(Center, "builtin:door", null, targetSceneId: null);

        door.SetGraphic("builtin:exit-door", " ");

        Assert.Equal("builtin:exit-door", door.GraphicKey);
        Assert.Null(door.UnlockedGraphicKey);
        Assert.Equal(DomainErrors.ValueRequired, Assert.Throws<DomainException>(() => door.SetGraphic(" ", null)).Code);
    }

    [Fact]
    public void Objects_KeepAnIdGivenByTheEditor()
    {
        var id = Guid.NewGuid();

        Assert.Equal(id, new ExitDoor(Center, "exit.png", null, id).Id);
        Assert.NotEqual(Guid.Empty, new ExitDoor(Center, "exit.png", null).Id);
    }
}
