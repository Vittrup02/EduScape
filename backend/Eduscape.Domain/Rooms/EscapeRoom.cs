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

    /// <summary>The teacher who owns the room; null for shared templates. Linked to accounts once auth exists.</summary>
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

    public void Rename(string title) => Title = Guard.Required(title, TextLimits.Title);

    public void Categorize(Subject subject, GradeLevel gradeLevel)
    {
        Subject = subject;
        GradeLevel = gradeLevel;
    }
}
