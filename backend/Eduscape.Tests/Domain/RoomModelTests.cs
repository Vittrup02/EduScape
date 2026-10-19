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
    public void Lock_MultipleChoiceAnswerMustBeAnOption()
    {
        var ex = Assert.Throws<DomainException>(() =>
            new Lock(Center, "lock.png", null, "?", AnswerType.MultipleChoice, "5", options: ["1", "2"]));

        Assert.Equal(DomainErrors.LockAnswerNotInOptions, ex.Code);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("1", " 1 ")]
    public void Lock_MultipleChoiceNeedsTwoDistinctOptions(params string[] options)
    {
        var ex = Assert.Throws<DomainException>(() =>
            new Lock(Center, "lock.png", null, "?", AnswerType.MultipleChoice, "1", options: options));

        Assert.Equal(DomainErrors.LockOptionsInvalid, ex.Code);
    }

    [Fact]
    public void Lock_RequiresQuestionAndAnswer()
    {
        Assert.Equal(
            DomainErrors.ValueRequired,
            Assert.Throws<DomainException>(() => new Lock(Center, "lock.png", null, " ", AnswerType.Code, "1")).Code);
        Assert.Equal(
            DomainErrors.ValueRequired,
            Assert.Throws<DomainException>(() => new Lock(Center, "lock.png", null, "?", AnswerType.Code, "")).Code);
    }
}
