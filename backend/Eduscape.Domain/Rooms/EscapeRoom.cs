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

    public const int DefaultWrongAnswerCooldownSeconds = 15;
    public const int MaxWrongAnswerCooldownSeconds = 300;

    /// <summary>
    /// How long a lock stays locked for a team after a wrong answer, so guessing doesn't pay; 0 turns it off.
    /// Enforced on the server.
    /// </summary>
    public int WrongAnswerCooldownSeconds { get; private set; } = DefaultWrongAnswerCooldownSeconds;

    public void SetWrongAnswerCooldown(int seconds)
    {
        if (seconds is < 0 or > MaxWrongAnswerCooldownSeconds)
        {
            throw new DomainException(DomainErrors.CooldownInvalid);
        }

        WrongAnswerCooldownSeconds = seconds;
    }

    /// <summary>Puts the scenes in the given order (e.g. after the teacher dragged one), which must name every scene.</summary>
    public void ReorderScenes(IReadOnlyList<Guid> sceneIds)
    {
        if (sceneIds.Count != _scenes.Count || _scenes.Any(s => !sceneIds.Contains(s.Id)))
        {
            throw new DomainException(DomainErrors.SceneNotFound);
        }

        var positions = sceneIds.Select((id, index) => (id, index)).ToDictionary(p => p.id, p => p.index);
        foreach (var scene in _scenes)
        {
            scene.SortOrder = positions[scene.Id];
        }

        _scenes.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));
    }

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

            if (roomObject is Doorway doorway)
            {
                foreach (var removed in removedObjects)
                {
                    doorway.ForgetLock(removed);
                }
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
    /// A copy of the room for a teacher to edit, typically of a template. Scenes and objects get new ids, and doors,
    /// their locks and unlockedBy references point into the copy.
    /// </summary>
    public EscapeRoom CopyFor(Guid teacherId)
    {
        var copy = new EscapeRoom(Title, Subject, GradeLevel, teacherId)
        {
            WrongAnswerCooldownSeconds = WrongAnswerCooldownSeconds,
        };
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

            if (original is Doorway doorway && objectCopy is Doorway doorwayCopy)
            {
                doorwayCopy.SetOpenCondition(
                    doorway.OpenCondition,
                    doorway.RequiredLockIds.Where(objectIds.ContainsKey).Select(lockId => objectIds[lockId]));
            }
        }

        return copy;
    }

    private static RoomObject CopyObject(RoomObject original, Dictionary<Guid, Guid> sceneIds) => original switch
    {
        // Steps are immutable, but each copy gets new step ids of its own.
        Lock l => new Lock(l.Position, l.GraphicKey, l.UnlockedGraphicKey, l.Steps.Select(CopyStep)),
        Letter letter => new Letter(letter.Position, letter.GraphicKey, letter.Text),
        Door door => new Door(
            door.Position,
            door.GraphicKey,
            door.UnlockedGraphicKey,
            door.TargetSceneId is { } target && sceneIds.TryGetValue(target, out var copiedTarget) ? copiedTarget : null),
        ExitDoor exit => new ExitDoor(exit.Position, exit.GraphicKey, exit.UnlockedGraphicKey),
        TextBox textBox => new TextBox(textBox.Position, textBox.Text),
        _ => throw new InvalidOperationException($"Unknown object type {original.Type}."),
    };

    private static LockStep CopyStep(LockStep step) => new(
        step.Question,
        step.AnswerType,
        step.CorrectAnswer,
        step.Tolerance,
        step.Hint,
        step.Options,
        step.AcceptedAnswers,
        step.AcceptTypos,
        step.OrderItems.Select(i => new OrderItem(Guid.NewGuid(), i.Text, i.ImageKey)));

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
    /// Finds problems that make the room unplayable: no start scene, unfinished locks (and their steps) and letters,
    /// broken or circular unlockedBy references, doors without a target scene in this room or without the locks they
    /// open with, and a room students can never escape from (no exit door, or locks and exit doors they can never reach).
    /// </summary>
    public IReadOnlyList<RoomValidationError> Validate()
    {
        var errors = new List<RoomValidationError>();
        if (StartScene is null)
        {
            errors.Add(new RoomValidationError(DomainErrors.StartSceneMissing, null));
        }

        if (!Objects.OfType<ExitDoor>().Any())
        {
            errors.Add(new RoomValidationError(DomainErrors.ExitDoorMissing, null));
        }

        var objects = Objects.ToDictionary(o => o.Id);
        var sceneIds = _scenes.Select(s => s.Id).ToHashSet();
        var brokenUnlockedBy = new HashSet<Guid>();

        foreach (var roomObject in objects.Values)
        {
            errors.AddRange(roomObject.ContentErrors().Select(code => new RoomValidationError(code, roomObject.Id)));
            if (roomObject is Lock @lock)
            {
                errors.AddRange(@lock.PuzzleErrors().Select(e => new RoomValidationError(e.Code, @lock.Id, e.StepIndex)));
            }

            if (roomObject is Doorway { OpenCondition: DoorOpenCondition.SelectedLocks } doorway
                && (doorway.RequiredLockIds.Count == 0
                    || doorway.RequiredLockIds.Any(id => !objects.TryGetValue(id, out var required) || required is not Lock)))
            {
                errors.Add(new RoomValidationError(DomainErrors.DoorLocksMissing, doorway.Id));
            }

            var unlockedByError = ValidateUnlockedBy(roomObject, objects);
            if (unlockedByError is not null)
            {
                errors.Add(new RoomValidationError(unlockedByError, roomObject.Id));
                brokenUnlockedBy.Add(roomObject.Id);
            }

            if (roomObject is Door door && (door.TargetSceneId is not { } target || !sceneIds.Contains(target)))
            {
                errors.Add(new RoomValidationError(DomainErrors.DoorTargetMissing, door.Id));
            }
        }

        // Objects with a broken unlockedBy are unreachable as well, but their own error already says why; they count as
        // solved here, so nothing that depends on them (e.g. the exit door in their scene) is reported a second time.
        if (StartScene is not null)
        {
            errors.AddRange(UnreachableObjects(brokenUnlockedBy)
                .Where(o => !brokenUnlockedBy.Contains(o.Id))
                .Select(o => new RoomValidationError(DomainErrors.ObjectUnreachable, o.Id)));
        }

        return errors;
    }

    /// <summary>
    /// Plays the room like a team that solves every lock it can see (the doors then open by themselves), until nothing
    /// new appears. Locks still out of reach, and exit doors that never open, mean the team can never finish: no door
    /// leads to their scene, or something waits for itself (e.g. a door that opens with a lock in the scene the door
    /// leads to).
    /// </summary>
    private IEnumerable<RoomObject> UnreachableObjects(IEnumerable<Guid> assumeSolved)
    {
        var solved = new HashSet<Guid>(assumeSolved);
        RoomProgress progress;
        bool progressed;
        do
        {
            progress = new RoomProgress(this, solved);
            progressed = false;
            foreach (var @lock in Objects.OfType<Lock>().Where(progress.IsAvailable))
            {
                progressed |= solved.Add(@lock.Id);
            }
        }
        while (progressed);

        return Objects.Where(o => o switch
        {
            Lock => !progress.IsAvailable(o),
            ExitDoor exit => !progress.CanOpenExitDoor(exit),
            _ => false,
        });
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
