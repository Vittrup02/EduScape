using System.Globalization;

namespace Eduscape.Domain.Rooms;

/// <summary>What a student submitted for a step: a typed or chosen answer, or the item ids in their order.</summary>
public sealed record AnswerSubmission(string? Text, IReadOnlyList<Guid>? Order = null)
{
    public static AnswerSubmission Typed(string? text) => new(text);

    public static AnswerSubmission Ordered(IReadOnlyList<Guid> order) => new(null, order);
}

/// <summary>Checks a submitted answer against a lock's step. Runs only on the server.</summary>
public static class AnswerMatcher
{
    /// <summary>Spelling mistakes are never accepted in answers shorter than this, so "Na" and "Ne" stay different.</summary>
    public const int MinLengthForTypos = 5;

    /// <summary>From this length, two mistakes are accepted instead of one.</summary>
    public const int MinLengthForTwoTypos = 10;

    private const NumberStyles DecimalStyle = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

    /// <summary>
    /// All typed answers are trimmed and compared case-insensitively. Text answers that are numbers on both sides are
    /// compared numerically, accepting ',' or '.' as decimal separator and the step's tolerance. Other text answers may
    /// match the answer or any accepted alternative, with small spelling mistakes if the teacher allows them
    /// (<see cref="TyposAllowed"/>). Codes are never treated as numbers and never forgive mistakes. An order is right
    /// only when every item is in its place.
    /// </summary>
    public static bool IsCorrect(LockStep step, AnswerSubmission submission)
    {
        if (step.AnswerType == AnswerType.Order)
        {
            return submission.Order is { } order && order.SequenceEqual(step.OrderItems.Select(i => i.Id));
        }

        if (string.IsNullOrWhiteSpace(submission.Text))
        {
            return false;
        }

        var answer = submission.Text.Trim();
        if (step.AnswerType != AnswerType.Text)
        {
            return string.Equals(answer, step.CorrectAnswer.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        return new[] { step.CorrectAnswer }.Concat(step.AcceptedAnswers)
            .Any(expected => MatchesText(answer, expected.Trim(), step));
    }

    /// <summary>How many spelling mistakes (edits) are forgiven in an answer whose correct form is this long.</summary>
    public static int TyposAllowed(int expectedLength) => expectedLength switch
    {
        >= MinLengthForTwoTypos => 2,
        >= MinLengthForTypos => 1,
        _ => 0,
    };

    private static bool MatchesText(string answer, string expected, LockStep step)
    {
        if (expected.Length == 0)
        {
            return false;
        }

        var expectedIsNumber = TryParseDecimal(expected, out var expectedNumber);
        if (expectedIsNumber && TryParseDecimal(answer, out var actual))
        {
            return Math.Abs(actual - expectedNumber) <= (step.Tolerance ?? 0m);
        }

        if (string.Equals(answer, expected, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Numbers are compared as numbers, never as text with a spelling mistake ("3.5" is not "3.6").
        return step.AcceptTypos
            && !expectedIsNumber
            && Distance(answer.ToLowerInvariant(), expected.ToLowerInvariant()) <= TyposAllowed(expected.Length);
    }

    /// <summary>Levenshtein distance: the fewest insertions, deletions and substitutions turning one into the other.</summary>
    private static int Distance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var substitution = previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                current[j] = Math.Min(substitution, Math.Min(previous[j] + 1, current[j - 1] + 1));
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    /// <summary>
    /// Parses a number with at most one ',' or '.' as decimal separator. Anything with thousands separators
    /// (e.g. "1.000,5") is rejected, because "1.000" is ambiguous between Danish and English notation.
    /// </summary>
    private static bool TryParseDecimal(string value, out decimal result)
    {
        result = 0;
        if (value.Count(c => c is ',' or '.') > 1)
        {
            return false;
        }

        return decimal.TryParse(value.Replace(',', '.'), DecimalStyle, CultureInfo.InvariantCulture, out result);
    }
}
