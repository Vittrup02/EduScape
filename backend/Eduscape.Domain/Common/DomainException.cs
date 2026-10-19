namespace Eduscape.Domain.Common;

/// <summary>
/// Thrown when a domain invariant is violated. Carries an error code (see <see cref="DomainErrors"/>)
/// that the frontend translates – never a user-facing sentence.
/// </summary>
public sealed class DomainException(string code) : Exception(code)
{
    public string Code { get; } = code;
}
