namespace Eduscape.Domain.Common;

internal static class Guard
{
    /// <summary>Returns the trimmed value, or throws if it is blank or longer than <paramref name="maxLength"/>.</summary>
    public static string Required(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException(DomainErrors.ValueRequired);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new DomainException(DomainErrors.ValueTooLong);
        }

        return trimmed;
    }

    /// <summary>
    /// Returns the trimmed value, or an empty string when blank; throws if longer than <paramref name="maxLength"/>.
    /// For content a teacher may leave unfinished while editing; <see cref="Rooms.EscapeRoom.Validate"/> reports it.
    /// </summary>
    public static string Draft(string? value, int maxLength)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length > maxLength)
        {
            throw new DomainException(DomainErrors.ValueTooLong);
        }

        return trimmed;
    }

    /// <summary>Returns the trimmed value or null when blank; throws if longer than <paramref name="maxLength"/>.</summary>
    public static string? Optional(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : Required(value, maxLength);
}
