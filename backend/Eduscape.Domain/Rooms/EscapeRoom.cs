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

    /// <summary>Increases with every save from the editor, so that two open editors cannot overwrite each other.</summary>
    public int Revision { get; private set; }

    /// <summary>Adds a scene. The first scene becomes the start scene.</summary>
    public Scene AddScene(string name, string backgroundImageKey, Guid? id = null)
    {
        var scene = new Scene(Id, name, backgroundImageKey, sortOrder: _scenes.Count, id)
        {
            IsStartScene = _scenes.Count == 0,
        };
        _scenes.Add(scene);
        return scene;
    }

    /// <summary>
    /// Removes a scene and its objects. Doors that led to it lose their target and objects that waited for one of its
    /// objects become visible from start, like the database does on delete; <see cref="Validate"/> reports the doors.
    /// If it was the start scene, the first remaining scene takes over.
    /// </summary>
    public void RemoveScene(Guid sceneId)
    {
        var scene = _scenes.Find(s => s.Id == sceneId) ?? throw new DomainException(DomainErrors.SceneNotFound);
        if (_scenes.Count == 1)
        {
            throw new DomainException(DomainErrors.LastScene);
        }

        _scenes.Remove(scene);
        var removedObjects = scene.Objects.Select(o => o.Id).ToHashSet();
        foreach (var roomObject in Objects)
        {
            if (roomObject.UnlockedById is { } sourceId && removedObjects.Contains(sourceId))
            {
                roomObject.SetUnlockedBy((Guid?)null);
            }

            if (roomObject is Door door && door.TargetSceneId == sceneId)
            {
                door.SetTarget(null);
            }
        }

        if (scene.IsStartScene)
        {
            _scenes[0].IsStartScene = true;
        }

        for (var i = 0; i < _scenes.Count; i++)
        {
            _scenes[i].SortOrder = i;
        }
    }

    public void MarkEdited() => Revision++;

    /// <summary>
    /// A copy of the room for a teacher to edit, typically of a template. Scenes and objects get new ids, and doors
    /// and unlockedBy references point into the copy.
    /// </summary>
    public EscapeRoom CopyFor(Guid teacherId)
    {
        var copy = new EscapeRoom(Title, Subject, GradeLevel, teacherId);
        var sceneIds = new Dictionary<Guid, Guid>();
        var objectIds = new Dictionary<Guid, Guid>();
        var copiedObjects = new List<(RoomObject Original, RoomObject Copy)>();

        foreach (var scene in _scenes.OrderBy(s => s.SortOrder))
        {
            var sceneCopy = copy.AddScene(scene.Name, scene.BackgroundImageKey);
            sceneCopy.IsStartScene = scene.IsStartScene;
            sceneCopy.SetNarration(scene.Narration);
            sceneIds[scene.Id] = sceneCopy.Id;
        }

        foreach (var scene in _scenes)
        {
            var sceneCopy = copy._scenes.Single(s => s.Id == sceneIds[scene.Id]);
            foreach (var original in scene.Objects)
            {
                var objectCopy = sceneCopy.AddObject(CopyObject(original, sceneIds));
                objectIds[original.Id] = objectCopy.Id;
                copiedObjects.Add((original, objectCopy));
            }
        }

        foreach (var (original, objectCopy) in copiedObjects)
        {
            objectCopy.SetUnlockedBy(original.UnlockedById is { } id && objectIds.TryGetValue(id, out var copiedId)
                ? copiedId
                : null);
        }

        return copy;
    }

    private static RoomObject CopyObject(RoomObject original, Dictionary<Guid, Guid> sceneIds) => original switch
    {
        Lock l => new Lock(
            l.Position, l.GraphicKey, l.UnlockedGraphicKey, l.Question, l.AnswerType, l.CorrectAnswer, l.Tolerance, l.Hint, l.Options),
        Letter letter => new Letter(letter.Position, letter.GraphicKey, letter.Text),
        Door door => new Door(
            door.Position,
            door.GraphicKey,
            door.UnlockedGraphicKey,
            door.TargetSceneId is { } target && sceneIds.TryGetValue(target, out var copiedTarget) ? copiedTarget : null),
        ExitDoor exit => new ExitDoor(exit.Position, exit.GraphicKey, exit.UnlockedGraphicKey),
        _ => throw new InvalidOperationException($"Unknown object type {original.Type}."),
    };

    /// <summary>Throws <see cref="DomainErrors.RoomInvalid"/> if <see cref="Validate"/> reports any problem.</summary>
    public void EnsurePlayable()
    {
        if (Validate().Count > 0)
        {
            throw new DomainException(DomainErrors.RoomInvalid);
        }
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
    /// Finds problems that make the room unplayable: no start scene, unfinished locks and letters, broken or circular
    /// unlockedBy references, and doors without a target scene in this room.
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
            errors.AddRange(roomObject.ContentErrors().Select(code => new RoomValidationError(code, roomObject.Id)));

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
