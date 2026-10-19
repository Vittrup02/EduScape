namespace Eduscape.Domain.Sessions;

public enum SessionMode
{
    /// <summary>Students pick a team after entering the PIN.</summary>
    Team,

    /// <summary>The server creates one team per student; otherwise identical to team mode.</summary>
    Individual,
}
