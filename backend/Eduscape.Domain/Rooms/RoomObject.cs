using Eduscape.Domain.Common;

namespace Eduscape.Domain.Rooms;

/// <summary>An object placed on a scene image: a lock, letter, door or exit door.</summary>
public abstract class RoomObject
{
    /// <param name="id">Set by the editor, which creates ids itself so that undo can restore an object as it was.</param>
    protected RoomObject(Position position, string graphicKey, string? unlockedGraphicKey, Guid? id)
    {
        Id = id ?? Guid.CreateVersion7();
        Position = position;
        SetGraphic(graphicKey, unlockedGraphicKey);
    }

    // For EF Core
    protected RoomObject()
    {
        Position = null!;
        GraphicKey = null!;
    }

    public Guid Id { get; private set; }

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

    /// <summary>Sets the reference by id; whether it points to a valid object is reported by <see cref="EscapeRoom.Validate"/>.</summary>
    public void SetUnlockedBy(Guid? sourceId) => UnlockedById = sourceId;

    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(GraphicKey))]
    public void SetGraphic(string graphicKey, string? unlockedGraphicKey)
    {
        GraphicKey = Guard.Required(graphicKey, TextLimits.StorageKey);
        UnlockedGraphicKey = Guard.Optional(unlockedGraphicKey, TextLimits.StorageKey);
    }

    /// <summary>Problems with the object's own content, such as a lock without a question.</summary>
    internal virtual IEnumerable<string> ContentErrors() => [];
}
