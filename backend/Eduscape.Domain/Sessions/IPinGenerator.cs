namespace Eduscape.Domain.Sessions;

/// <summary>Draws candidate PINs. Uniqueness among active sessions is checked by the caller.</summary>
public interface IPinGenerator
{
    string Next();
}
