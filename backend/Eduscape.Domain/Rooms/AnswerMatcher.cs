using System.Globalization;

namespace Eduscape.Domain.Rooms;

/// <summary>Checks a submitted answer against a lock. Runs only on the server.</summary>
public static class AnswerMatcher
{
    private const NumberStyles DecimalStyle = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

    /// <summary>
    /// All answer types are trimmed and compared case-insensitively. Text answers that are numbers on both sides
    /// are compared numerically, accepting ',' or '.' as decimal separator and the lock's tolerance.
    /// Codes are never treated as numbers, so leading zeros matter.
    /// </summary>
    public static bool IsCorrect(Lock @lock, string? submitted)
    {
        if (string.IsNullOrWhiteSpace(submitted))
        {
            return false;
        }

        var answer = submitted.Trim();
        var correct = @lock.CorrectAnswer.Trim();

        if (@lock.AnswerType == AnswerType.Text
            && TryParseDecimal(correct, out var expected)
            && TryParseDecimal(answer, out var actual))
        {
            return Math.Abs(actual - expected) <= (@lock.Tolerance ?? 0m);
        }

        return string.Equals(answer, correct, StringComparison.OrdinalIgnoreCase);
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
