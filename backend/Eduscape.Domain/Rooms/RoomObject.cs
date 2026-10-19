using Eduscape.Domain.Common;

namespace Eduscape.Domain.Rooms;

/// <summary>An object placed on a scene image: a lock, letter, door or exit door.</summary>
public abstract class RoomObject
{
    protected RoomObject(Position position, string graphicKey, string? unlockedGraphicKey)
    {
        Position = position;
        GraphicKey = Guard.Required(graphicKey, TextLimits.StorageKey);
        UnlockedGraphicKey = Guard.Optional(unlockedGraphicKey, TextLimits.StorageKey);
    }

    // For EF Core
    protected RoomObject()
    {
        Position = null!;
        GraphicKey = null!;
    }

    public Guid Id { get; private set; } = Guid.CreateVersion7();

    public Guid SceneId { get; internal set; }

    public abstract RoomObjectType Type { get; }

    public Position Position { get; private set; }

    /// <summary>
    /// Graphic shown in the locked (or only) state: a built-in asset key ("builtin:padlock", see
    /// frontend/src/assets/ASSETS.md) or the R2 object key of an uploaded image.
    /// </summary>
    public string GraphicKey { get; private set; }

    /// <summary>
    /// R2 object key of an uploaded graphic shown once the object is solved/opened. Null for built-in assets,
    /// which bring their own open state.
    /// </summary>
    public string? UnlockedGraphicKey { get; private set; }

    /// <summary>The object that must be completed before this one is available; null means visible from start.</summary>
    public Guid? UnlockedById { get; private set; }

    public void MoveTo(Position position) => Position = position;

    public void SetUnlockedBy(RoomObject? source) => UnlockedById = source?.Id;
}
