namespace Eduscape.Domain.Rooms;

/// <summary>Leads to another scene in the same room.</summary>
public sealed class Door : RoomObject
{
    public Door(Position position, string graphicKey, string? unlockedGraphicKey, Guid? targetSceneId, Guid? id = null)
        : base(position, graphicKey, unlockedGraphicKey, id)
    {
        TargetSceneId = targetSceneId;
    }

    // For EF Core
    private Door()
    {
    }

    public override RoomObjectType Type => RoomObjectType.Door;

    /// <summary>
    /// Null while the teacher has not chosen a scene, or when the target scene has been deleted;
    /// reported by <see cref="EscapeRoom.Validate"/>.
    /// </summary>
    public Guid? TargetSceneId { get; private set; }

    public void SetTarget(Guid? targetSceneId) => TargetSceneId = targetSceneId;
}
