using Eduscape.Domain.Rooms;

namespace Eduscape.Tests.Domain;

public class AnswerMatcherTests
{
    private static readonly Position Center = new(50, 50);

    private static Lock TextLock(string correct, decimal? tolerance = null) =>
        new(Center, "lock.png", null, "?", AnswerType.Text, correct, tolerance);

    private static Lock CodeLock(string correct) =>
        new(Center, "lock.png", null, "?", AnswerType.Code, correct);

    [Theory]
    [InlineData("Paris", "Paris")]
    [InlineData("Paris", "  paris ")]
    [InlineData("København", "KØBENHAVN")]
    [InlineData("Ærø", "ærø")]
    public void Text_IsTrimmedAndCaseInsensitive(string correct, string submitted) =>
        Assert.True(AnswerMatcher.IsCorrect(TextLock(correct), submitted));

    [Theory]
    [InlineData("Paris", "Pariss")]
    [InlineData("Paris", "")]
    [InlineData("Paris", "   ")]
    [InlineData("Paris", null)]
    public void Text_WrongOrEmpty_IsIncorrect(string correct, string? submitted) =>
        Assert.False(AnswerMatcher.IsCorrect(TextLock(correct), submitted));

    [Theory]
    [InlineData("1,5", "1.5")]
    [InlineData("1.5", "1,5")]
    [InlineData("1,5", "1,50")]
    [InlineData("2", "2,0")]
    [InlineData("-3,25", " -3.25 ")]
    [InlineData("0,5", ",5")]
    public void Text_NumbersWithCommaOrPoint_AreComparedNumerically(string correct, string submitted) =>
        Assert.True(AnswerMatcher.IsCorrect(TextLock(correct), submitted));

    [Fact]
    public void Text_NumberWithoutTolerance_MustBeExact() =>
        Assert.False(AnswerMatcher.IsCorrect(TextLock("3,33"), "3,334"));

    [Theory]
    [InlineData("3,33", true)]
    [InlineData("3.333", true)]
    [InlineData("3,34", true)] // boundary is inclusive
    [InlineData("3,32", true)]
    [InlineData("3,345", false)]
    [InlineData("3,3", false)]
    public void Text_NumberWithinTolerance_IsCorrect(string submitted, bool expected) =>
        Assert.Equal(expected, AnswerMatcher.IsCorrect(TextLock("3,33", tolerance: 0.01m), submitted));

    [Theory]
    [InlineData("1.000", "1000")] // thousands separators are not interpreted
    [InlineData("1.000,5", "1000.5")]
    [InlineData("1 000", "1000")]
    public void Text_AmbiguousNumbers_FallBackToTextComparison(string correct, string submitted) =>
        Assert.False(AnswerMatcher.IsCorrect(TextLock(correct), submitted));

    [Fact]
    public void Text_NumericCorrectAnswer_TextSubmitted_IsIncorrect() =>
        Assert.False(AnswerMatcher.IsCorrect(TextLock("17", tolerance: 1m), "sytten"));

    [Theory]
    [InlineData("0427", "0427", true)]
    [InlineData("0427", " 0427 ", true)]
    [InlineData("0427", "427", false)] // leading zeros matter in codes
    [InlineData("1,5", "1.5", false)] // no numeric normalisation for codes
    [InlineData("ABC", "abc", true)]
    public void Code_IsTrimmedAndCaseInsensitiveButNotNumeric(string correct, string submitted, bool expected) =>
        Assert.Equal(expected, AnswerMatcher.IsCorrect(CodeLock(correct), submitted));

    [Theory]
    [InlineData("17", true)]
    [InlineData(" 17 ", true)]
    [InlineData("15", false)]
    public void MultipleChoice_MatchesChosenOption(string submitted, bool expected)
    {
        var @lock = new Lock(Center, "lock.png", null, "Primtal?", AnswerType.MultipleChoice, "17", options: ["9", "15", "17", "21"]);

        Assert.Equal(expected, AnswerMatcher.IsCorrect(@lock, submitted));
    }
}
