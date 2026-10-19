using Eduscape.Api.Errors;
using Eduscape.Domain.Common;
using Eduscape.Domain.Rooms;

namespace Eduscape.Api.Editor;

/// <summary>
/// Applies the editor's whole-room document to a loaded room through the domain's own methods. The document is checked
/// as a whole first, so a request that cannot be applied changes nothing. Problems a teacher can still be working on
/// (an empty question, a door without a target) are allowed and reported by <see cref="EscapeRoom.Validate"/>.
/// </summary>
public static class RoomDocumentApplier
{
    public const int MaxScenes = 20;
    public const int MaxObjectsPerScene = 60;

    /// <summary>Ids in the document that the room does not have yet, i.e. the scenes and objects to create.</summary>
    public static IReadOnlyCollection<Guid> NewIds(EscapeRoom room, SaveRoomRequest request)
    {
        var existing = room.Scenes.Select(s => s.Id).Concat(room.Objects.Select(o => o.Id)).ToHashSet();
        return [.. AllIds(request).Where(id => !existing.Contains(id))];
    }

    /// <summary>Throws a <see cref="DomainException"/> with the reason when the document cannot be applied.</summary>
    public static void Apply(EscapeRoom room, SaveRoomRequest request)
    {
        Check(room, request);

        room.Rename(request.Title);
        room.Categorize(request.Subject, request.GradeLevel);

        // New scenes first, so that removing the old ones never removes the last scene.
        var existingScenes = room.Scenes.Select(s => s.Id).ToHashSet();
        foreach (var scene in request.Scenes.Where(s => !existingScenes.Contains(s.Id)))
        {
            room.AddScene(scene.Name, scene.BackgroundImageKey, scene.Id);
        }

        var keptScenes = request.Scenes.Select(s => s.Id).ToHashSet();
        foreach (var removed in room.Scenes.Where(s => !keptScenes.Contains(s.Id)).Select(s => s.Id).ToList())
        {
            room.RemoveScene(removed);
        }

        room.SetStartScene(request.StartSceneId);

        var objects = room.Objects.ToDictionary(o => o.Id);
        foreach (var sceneDocument in request.Scenes)
        {
            var scene = room.Scenes.Single(s => s.Id == sceneDocument.Id);
            scene.Rename(sceneDocument.Name);
            scene.SetBackgroundImage(sceneDocument.BackgroundImageKey);

            var keptObjects = sceneDocument.Objects.Select(o => o.Id).ToHashSet();
            foreach (var removed in scene.Objects.Where(o => !keptObjects.Contains(o.Id)).Select(o => o.Id).ToList())
            {
                scene.RemoveObject(removed);
            }

            foreach (var document in sceneDocument.Objects)
            {
                if (objects.TryGetValue(document.Id, out var existing))
                {
                    Update(existing, document);
                }
                else
                {
                    objects[document.Id] = scene.AddObject(Create(document));
                }
            }
        }

        // References last, once every object they may point to exists.
        foreach (var document in request.Scenes.SelectMany(s => s.Objects))
        {
            objects[document.Id].SetUnlockedBy(document.UnlockedById);
        }

        room.MarkEdited();
    }

    private static void Check(EscapeRoom room, SaveRoomRequest request)
    {
        if (request.Scenes is not { Length: > 0 } || request.Scenes.Any(s => s?.Objects is null))
        {
            throw Invalid();
        }

        if (request.Scenes.Length > MaxScenes || request.Scenes.Any(s => s.Objects.Length > MaxObjectsPerScene))
        {
            throw new DomainException(ApiErrors.RoomTooLarge);
        }

        var ids = AllIds(request).ToList();
        if (ids.Contains(Guid.Empty) || ids.Distinct().Count() != ids.Count || request.Scenes.Any(s => s.Objects.Any(o => o is null)))
        {
            throw Invalid();
        }

        var sceneIds = request.Scenes.Select(s => s.Id).ToHashSet();
        var objectIds = request.Scenes.SelectMany(s => s.Objects).Select(o => o.Id).ToHashSet();
        if (!sceneIds.Contains(request.StartSceneId))
        {
            throw Invalid();
        }

        var existing = room.Scenes.SelectMany(s => s.Objects).ToDictionary(o => o.Id);
        foreach (var scene in request.Scenes)
        {
            RequireImageKey(scene.BackgroundImageKey);
            foreach (var document in scene.Objects)
            {
                RequireImageKey(document.GraphicKey);

                // References must stay inside the document; the database enforces them as foreign keys.
                var validReferences = (document.UnlockedById is not { } source || objectIds.Contains(source))
                    && (document.TargetSceneId is not { } target || sceneIds.Contains(target));
                var validShape = Enum.IsDefined(document.Type)
                    && (document.Type != RoomObjectType.Lock || document.AnswerType is { } answerType && Enum.IsDefined(answerType));

                // An object keeps its type and scene; the editor never changes either.
                var sameAsBefore = !existing.TryGetValue(document.Id, out var before)
                    || (before.Type == document.Type && before.SceneId == scene.Id);

                if (!validReferences || !validShape || !sameAsBefore)
                {
                    throw Invalid();
                }
            }
        }
    }

    private static RoomObject Create(EditorObjectDto document)
    {
        var position = new Position(document.X, document.Y);
        return document.Type switch
        {
            RoomObjectType.Lock => new Lock(
                position,
                document.GraphicKey,
                unlockedGraphicKey: null,
                document.Question ?? string.Empty,
                document.AnswerType!.Value,
                document.CorrectAnswer ?? string.Empty,
                ToleranceFor(document),
                document.Hint,
                OptionsFor(document),
                document.Id),
            RoomObjectType.Letter => new Letter(position, document.GraphicKey, document.Text ?? string.Empty, document.Id),
            RoomObjectType.Door => new Door(position, document.GraphicKey, null, document.TargetSceneId, document.Id),
            RoomObjectType.ExitDoor => new ExitDoor(position, document.GraphicKey, null, document.Id),
            _ => throw Invalid(),
        };
    }

    private static void Update(RoomObject roomObject, EditorObjectDto document)
    {
        roomObject.MoveTo(new Position(document.X, document.Y));
        roomObject.SetGraphic(document.GraphicKey, unlockedGraphicKey: null);
        switch (roomObject)
        {
            case Lock @lock:
                @lock.SetQuestion(document.Question ?? string.Empty, document.Hint);
                @lock.SetAnswer(
                    document.AnswerType!.Value, document.CorrectAnswer ?? string.Empty, ToleranceFor(document), OptionsFor(document));
                break;
            case Letter letter:
                letter.SetText(document.Text ?? string.Empty);
                break;
            case Door door:
                door.SetTarget(document.TargetSceneId);
                break;
        }
    }

    // The editor keeps a lock's options and tolerance while the teacher switches answer type, so a switch back
    // restores them; only the ones that apply to the chosen type are stored.
    private static decimal? ToleranceFor(EditorObjectDto document) =>
        document.AnswerType == AnswerType.Text ? document.Tolerance : null;

    private static string[]? OptionsFor(EditorObjectDto document) =>
        document.AnswerType == AnswerType.MultipleChoice ? document.Options : null;

    private static IEnumerable<Guid> AllIds(SaveRoomRequest request) =>
        (request.Scenes ?? []).Select(s => s.Id).Concat((request.Scenes ?? []).SelectMany(s => s.Objects ?? []).Select(o => o.Id));

    private static void RequireImageKey(string? key)
    {
        if (!ImageKeys.IsValid(key))
        {
            throw new DomainException(DomainErrors.ImageKeyInvalid);
        }
    }

    private static DomainException Invalid() => new(ApiErrors.DocumentInvalid);
}
