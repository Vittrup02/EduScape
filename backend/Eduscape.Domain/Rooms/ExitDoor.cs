namespace Eduscape.Domain.Rooms;

/// <summary>
/// Opens like any door (see <see cref="Doorway"/>); walking through it completes the room for the team.
/// </summary>
public sealed class ExitDoor : Doorway
{
    public ExitDoor(Position position, string graphicKey, string? unlockedGraphicKey, Guid? id = null)
        : base(position, graphicKey, unlockedGraphicKey, id)
    {
    }

    // For EF Core
    private ExitDoor()
    {
    }

    public override RoomObjectType Type => RoomObjectType.ExitDoor;
}
