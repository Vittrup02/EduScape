using Eduscape.Domain.Common;

namespace Eduscape.Domain.Rooms;

/// <summary>A background image with objects placed on it.</summary>
public sealed class Scene
{
    private readonly List<RoomObject> _objects = [];

    internal Scene(Guid escapeRoomId, string name, string backgroundImageKey, int sortOrder)
    {
        EscapeRoomId = escapeRoomId;
        Name = Guard.Required(name, TextLimits.Name);
        BackgroundImageKey = Guard.Required(backgroundImageKey, TextLimits.StorageKey);
        SortOrder = sortOrder;
    }

    // For EF Core
    private Scene()
    {
        Name = null!;
        BackgroundImageKey = null!;
    }

    public Guid Id { get; private set; } = Guid.CreateVersion7();

    public Guid EscapeRoomId { get; private set; }

    public string Name { get; private set; }

    /// <summary>A built-in background key ("builtin:classroom") or the R2 object key of an uploaded image.</summary>
    public string BackgroundImageKey { get; private set; }

    public int SortOrder { get; private set; }

    public bool IsStartScene { get; internal set; }

    public IReadOnlyList<RoomObject> Objects => _objects;

    public T AddObject<T>(T roomObject)
        where T : RoomObject
    {
        roomObject.SceneId = Id;
        _objects.Add(roomObject);
        return roomObject;
    }

    public void RemoveObject(Guid objectId) => _objects.RemoveAll(o => o.Id == objectId);

    public void Rename(string name) => Name = Guard.Required(name, TextLimits.Name);

    public void SetBackgroundImage(string backgroundImageKey) =>
        BackgroundImageKey = Guard.Required(backgroundImageKey, TextLimits.StorageKey);
}
