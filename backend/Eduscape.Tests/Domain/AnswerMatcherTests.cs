using Eduscape.Domain.Rooms;

namespace Eduscape.Tests.Domain;

public class AnswerMatcherTests
{
    private static LockStep TextStep(
        string correct, decimal? tolerance = null, string[]? accepted = null, bool acceptTypos = false) =>
        new("?", AnswerType.Text, correct, tolerance, acceptedAnswers: accepted, acceptTypos: acceptTypos);

    private static LockStep CodeStep(string correct) => new("?", AnswerType.Code, correct);

    private static bool IsCorrect(LockStep step, string? submitted) =>
        AnswerMatcher.IsCorrect(step, AnswerSubmission.Typed(submitted));

    [Theory]
    [InlineData("Paris", "Paris")]
    [InlineData("Paris", "  paris ")]
    [InlineData("København", "KØBENHAVN")]
    [InlineData("Ærø", "ærø")]
    public void Text_IsTrimmedAndCaseInsensitive(string correct, string submitted) =>
        Assert.True(IsCorrect(TextStep(correct), submitted));

    [Theory]
    [InlineData("Paris", "Pariss")]
    [InlineData("Paris", "")]
    [InlineData("Paris", "   ")]
    [InlineData("Paris", null)]
    public void Text_WrongOrEmpty_IsIncorrect(string correct, string? submitted) =>
        Assert.False(IsCorrect(TextStep(correct), submitted));

    [Theory]
    [InlineData("1,5", "1.5")]
    [InlineData("1.5", "1,5")]
    [InlineData("1,5", "1,50")]
    [InlineData("2", "2,0")]
    [InlineData("-3,25", " -3.25 ")]
    [InlineData("0,5", ",5")]
    public void Text_NumbersWithCommaOrPoint_AreComparedNumerically(string correct, string submitted) =>
        Assert.True(IsCorrect(TextStep(correct), submitted));

    [Fact]
    public void Text_NumberWithoutTolerance_MustBeExact() =>
        Assert.False(IsCorrect(TextStep("3,33"), "3,334"));

    [Theory]
    [InlineData("3,33", true)]
    [InlineData("3.333", true)]
    [InlineData("3,34", true)] // boundary is inclusive
    [InlineData("3,32", true)]
    [InlineData("3,345", false)]
    [InlineData("3,3", false)]
    public void Text_NumberWithinTolerance_IsCorrect(string submitted, bool expected) =>
        Assert.Equal(expected, IsCorrect(TextStep("3,33", tolerance: 0.01m), submitted));

    [Theory]
    [InlineData("12,5", true)]
    [InlineData("12.5", true)]
    [InlineData("13,5", true)] // ± 1,0 includes the boundary
    [InlineData("11.5", true)]
    [InlineData("13,6", false)]
    [InlineData("11,4", false)]
    public void Text_ToleranceOfOne_AcceptsCommaAndPoint(string submitted, bool expected) =>
        Assert.Equal(expected, IsCorrect(TextStep("12,5", tolerance: 1.0m), submitted));

    [Theory]
    [InlineData("1.000", "1000")] // thousands separators are not interpreted
    [InlineData("1.000,5", "1000.5")]
    [InlineData("1 000", "1000")]
    public void Text_AmbiguousNumbers_FallBackToTextComparison(string correct, string submitted) =>
        Assert.False(IsCorrect(TextStep(correct), submitted));

    [Fact]
    public void Text_NumericCorrectAnswer_TextSubmitted_IsIncorrect() =>
        Assert.False(IsCorrect(TextStep("17", tolerance: 1m), "sytten"));

    [Theory]
    [InlineData("vand")]
    [InlineData("H2O")]
    [InlineData("dihydrogenoxid")]
    public void Text_AcceptsEveryAlternativeTheTeacherGave(string submitted) =>
        Assert.True(IsCorrect(TextStep("vand", accepted: ["H2O", " Dihydrogenoxid "]), submitted));

    [Fact]
    public void Text_AlternativeThatIsANumber_IsComparedAsANumber() =>
        Assert.True(IsCorrect(TextStep("en halv", accepted: ["0,5"]), "0.50"));

    [Theory]
    [InlineData("Natrium", "Natriun", true)] // 7 letters: one mistake
    [InlineData("Natrium", "Natrum", true)] // a missing letter
    [InlineData("Natrium", "Natrriumm", false)] // two mistakes are too many under 10 letters
    [InlineData("Kalium", "Kalimu", false)] // two swapped letters count as two mistakes
    [InlineData("Klor", "Klar", false)] // under 5 letters: no mistakes
    [InlineData("Natriumklorid", "Natriumklorit", true)] // 13 letters: two mistakes
    [InlineData("Natriumklorid", "Natrimklorit", true)]
    [InlineData("Natriumklorid", "Natrimklorat", false)]
    public void Text_WithTyposAllowed_ForgivesSmallMistakesByLength(string correct, string submitted, bool expected) =>
        Assert.Equal(expected, IsCorrect(TextStep(correct, acceptTypos: true), submitted));

    [Theory]
    [InlineData("Na", "Ne")] // chemistry: two letters never forgive a mistake
    [InlineData("Fe", "F")]
    [InlineData("Guld", "Gul")]
    public void Text_ShortAnswers_NeverForgiveMistakes(string correct, string submitted) =>
        Assert.False(IsCorrect(TextStep(correct, acceptTypos: true), submitted));

    [Theory]
    [InlineData(4, 0)]
    [InlineData(5, 1)]
    [InlineData(9, 1)]
    [InlineData(10, 2)]
    public void TyposAllowed_DependOnTheLengthOfTheCorrectAnswer(int length, int typos) =>
        Assert.Equal(typos, AnswerMatcher.TyposAllowed(length));

    [Fact]
    public void Text_WithoutTyposAllowed_MustBeSpelledRight() =>
        Assert.False(IsCorrect(TextStep("Natrium"), "Natriun"));

    [Fact]
    public void Text_TyposAreForgivenInAlternativesToo() =>
        Assert.True(IsCorrect(TextStep("vand", accepted: ["dihydrogenoxid"], acceptTypos: true), "dihydrogenoxyd"));

    [Fact]
    public void Text_NumbersNeverCountAsSpellingMistakes() =>
        Assert.False(IsCorrect(TextStep("123456", acceptTypos: true), "123457"));

    [Theory]
    [InlineData("0427", "0427", true)]
    [InlineData("0427", " 0427 ", true)]
    [InlineData("0427", "427", false)] // leading zeros matter in codes
    [InlineData("1,5", "1.5", false)] // no numeric normalisation for codes
    [InlineData("ABC", "abc", true)]
    public void Code_IsTrimmedAndCaseInsensitiveButNotNumeric(string correct, string submitted, bool expected) =>
        Assert.Equal(expected, IsCorrect(CodeStep(correct), submitted));

    [Theory]
    [InlineData("17", true)]
    [InlineData(" 17 ", true)]
    [InlineData("15", false)]
    public void MultipleChoice_MatchesChosenOption(string submitted, bool expected)
    {
        var step = new LockStep("Primtal?", AnswerType.MultipleChoice, "17", options: ["9", "15", "17", "21"]);

        Assert.Equal(expected, IsCorrect(step, submitted));
    }

    [Fact]
    public void Order_IsCorrectOnlyWithEveryItemInItsPlace()
    {
        OrderItem[] items = [new(Guid.NewGuid(), "Afvej"), new(Guid.NewGuid(), "Opløs"), new(Guid.NewGuid(), "Filtrér")];
        var step = new LockStep("Rækkefølgen?", AnswerType.Order, string.Empty, orderItems: items);
        var ids = items.Select(i => i.Id).ToArray();

        Assert.True(AnswerMatcher.IsCorrect(step, AnswerSubmission.Ordered(ids)));
        Assert.False(AnswerMatcher.IsCorrect(step, AnswerSubmission.Ordered([ids[1], ids[0], ids[2]])));
        Assert.False(AnswerMatcher.IsCorrect(step, AnswerSubmission.Ordered(ids[..2])));
        Assert.False(AnswerMatcher.IsCorrect(step, AnswerSubmission.Typed("Afvej")));
    }
}
