namespace Eduscape.Domain.Sessions;

public enum SessionStatus
{
    /// <summary>Students can join and play.</summary>
    Open,

    /// <summary>No new students can join; existing students keep playing.</summary>
    Locked,

    /// <summary>Finished. The PIN is released and student data is scheduled for deletion.</summary>
    Closed,
}
