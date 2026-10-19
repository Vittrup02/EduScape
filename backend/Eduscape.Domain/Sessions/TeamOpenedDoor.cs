namespace Eduscape.Domain.Sessions;

/// <summary>A door the team has opened, which unlocks its target scene for every team member.</summary>
public sealed class TeamOpenedDoor
{
    internal TeamOpenedDoor(Guid teamId, Guid doorId, DateTimeOffset openedAt)
    {
        TeamId = teamId;
        DoorId = doorId;
        OpenedAt = openedAt;
    }

    // For EF Core
    private TeamOpenedDoor()
    {
    }

    public Guid TeamId { get; private set; }

    public Guid DoorId { get; private set; }

    public DateTimeOffset OpenedAt { get; private set; }
}
