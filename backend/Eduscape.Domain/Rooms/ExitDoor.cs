namespace Eduscape.Domain.Rooms;

/// <summary>Opens only when every lock in the room is solved, and completes the room.</summary>
public sealed class ExitDoor : RoomObject
{
    public ExitDoor(Position position, string graphicKey, string? unlockedGraphicKey)
        : base(position, graphicKey, unlockedGraphicKey)
    {
    }

    // For EF Core
    private ExitDoor()
    {
    }

    public override RoomObjectType Type => RoomObjectType.ExitDoor;
}
