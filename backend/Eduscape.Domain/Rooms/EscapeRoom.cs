using Eduscape.Domain.Common;

namespace Eduscape.Domain.Rooms;

/// <summary>
/// The content a teacher builds: scenes with locks, letters and doors. Subject and grade level belong here;
/// the class it is played with belongs to the session. Templates are ordinary rooms with <see cref="IsTemplate"/> set.
/// </summary>
public sealed class EscapeRoom
{
    private readonly List<Scene> _scenes = [];

    public EscapeRoom(
        string title,
        Subject subject,
        GradeLevel gradeLevel,
        Guid? ownerTeacherId = null,
        bool isTemplate = false,
        Guid? id = null)
    {
        Id = id ?? Guid.CreateVersion7();
        Title = Guard.Required(title, TextLimits.Title);
        Subject = subject;
        GradeLevel = gradeLevel;
        OwnerTeacherId = ownerTeacherId;
        IsTemplate = isTemplate;
    }

    // For EF Core
    private EscapeRoom() => Title = null!;

    public Guid Id { get; private set; }

    public string Title { get; private set; }

    public Subject Subject { get; private set; }

    public GradeLevel GradeLevel { get; private set; }

    public bool IsTemplate { get; private set; }

    /// <summary>The id of the teacher account that owns the room; null for shared templates.</summary>
    public Guid? OwnerTeacherId { get; private set; }

    public IReadOnlyList<Scene> Scenes => _scenes;

    public Scene? StartScene => _scenes.SingleOrDefault(s => s.IsStartScene);

    public IEnumerable<RoomObject> Objects => _scenes.SelectMany(s => s.Objects);

    /// <summary>Adds a scene. The first scene becomes the start scene.</summary>
    public Scene AddScene(string name, string backgroundImageKey)
    {
        var scene = new Scene(Id, name, backgroundImageKey, sortOrder: _scenes.Count)
        {
            IsStartScene = _scenes.Count == 0,
        };
        _scenes.Add(scene);
        return scene;
    }

    public void SetStartScene(Guid sceneId)
    {
        if (_scenes.All(s => s.Id != sceneId))
        {
            throw new DomainException(DomainErrors.SceneNotFound);
        }

        foreach (var scene in _scenes)
        {
            scene.IsStartScene = scene.Id == sceneId;
        }
    }

    /// <summary>
    /// Finds problems that make the room unplayable: no start scene, broken or circular unlockedBy references,
    /// and doors without a target scene in this room.
    /// </summary>
    public IReadOnlyList<RoomValidationError> Validate()
    {
        var errors = new List<RoomValidationError>();
        if (StartScene is null)
        {
            errors.Add(new RoomValidationError(DomainErrors.StartSceneMissing, null));
        }

        var objects = Objects.ToDictionary(o => o.Id);
        var sceneIds = _scenes.Select(s => s.Id).ToHashSet();

        foreach (var roomObject in objects.Values)
        {
            var unlockedByError = ValidateUnlockedBy(roomObject, objects);
            if (unlockedByError is not null)
            {
                errors.Add(new RoomValidationError(unlockedByError, roomObject.Id));
            }

            if (roomObject is Door door && (door.TargetSceneId is not { } target || !sceneIds.Contains(target)))
            {
                errors.Add(new RoomValidationError(DomainErrors.DoorTargetMissing, door.Id));
            }
        }

        return errors;
    }

    private static string? ValidateUnlockedBy(RoomObject roomObject, Dictionary<Guid, RoomObject> objects)
    {
        if (roomObject.UnlockedById is not { } sourceId)
        {
            return null;
        }

        if (sourceId == roomObject.Id)
        {
            return DomainErrors.UnlockedBySelf;
        }

        if (!objects.TryGetValue(sourceId, out var source))
        {
            return DomainErrors.UnlockedByNotFound;
        }

        if (source is not (Lock or Door))
        {
            return DomainErrors.UnlockedByInvalidTarget;
        }

        return IsInCycle(roomObject, objects) ? DomainErrors.UnlockedByCycle : null;
    }

    /// <summary>Follows the unlockedBy chain; each object has at most one, so the chain is a simple path.</summary>
    private static bool IsInCycle(RoomObject start, Dictionary<Guid, RoomObject> objects)
    {
        var visited = new HashSet<Guid>();
        var current = start;
        while (current.UnlockedById is { } nextId && objects.TryGetValue(nextId, out var next))
        {
            if (next.Id == start.Id)
            {
                return true;
            }

            if (!visited.Add(next.Id))
            {
                // A cycle further down the chain that does not include start.
                return false;
            }

            current = next;
        }

        return false;
    }

    public void Rename(string title) => Title = Guard.Required(title, TextLimits.Title);

    public void Categorize(Subject subject, GradeLevel gradeLevel)
    {
        Subject = subject;
        GradeLevel = gradeLevel;
    }
}
