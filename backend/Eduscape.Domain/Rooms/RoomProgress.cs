using Eduscape.Domain.Sessions;

namespace Eduscape.Domain.Rooms;

/// <summary>
/// Evaluates the unlockedBy rules for a team's progress in a room: which objects are available,
/// which scenes are unlocked and whether the exit door can be opened.
/// </summary>
public sealed class RoomProgress
{
    private readonly EscapeRoom _room;
    private readonly IReadOnlySet<Guid> _solvedLockIds;
    private readonly IReadOnlySet<Guid> _openedDoorIds;
    private readonly Dictionary<Guid, RoomObject> _objects;

    public RoomProgress(EscapeRoom room, IReadOnlySet<Guid> solvedLockIds, IReadOnlySet<Guid> openedDoorIds)
    {
        _room = room;
        _solvedLockIds = solvedLockIds;
        _openedDoorIds = openedDoorIds;
        _objects = room.Objects.ToDictionary(o => o.Id);
    }

    public static RoomProgress For(EscapeRoom room, Team team) => new(room, team.SolvedLockIds, team.OpenedDoorIds);

    /// <summary>A lock is completed when solved, a door when opened. Letters and exit doors never complete anything.</summary>
    public bool IsCompleted(RoomObject roomObject) => roomObject switch
    {
        Lock => _solvedLockIds.Contains(roomObject.Id),
        Door => _openedDoorIds.Contains(roomObject.Id),
        _ => false,
    };

    /// <summary>
    /// The object is visible and can be interacted with: its scene is unlocked and the object it is
    /// unlocked by (if any) is completed.
    /// </summary>
    public bool IsAvailable(RoomObject roomObject)
    {
        if (!IsSceneUnlocked(roomObject.SceneId))
        {
            return false;
        }

        return roomObject.UnlockedById is not { } sourceId
            || (_objects.TryGetValue(sourceId, out var source) && IsCompleted(source));
    }

    public bool CanOpenExitDoor(ExitDoor exitDoor) =>
        IsAvailable(exitDoor) && _room.Objects.OfType<Lock>().All(l => _solvedLockIds.Contains(l.Id));

    /// <summary>The start scene, and every scene behind a door the team has opened.</summary>
    public bool IsSceneUnlocked(Guid sceneId) =>
        _room.StartScene?.Id == sceneId
        || _room.Objects.OfType<Door>().Any(d => d.TargetSceneId == sceneId && _openedDoorIds.Contains(d.Id));
}
