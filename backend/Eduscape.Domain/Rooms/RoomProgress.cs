using Eduscape.Domain.Sessions;

namespace Eduscape.Domain.Rooms;

/// <summary>
/// What a team's solved locks mean in a room: which doors are open, which scenes are unlocked and which objects are
/// available. Doors are not stored as opened; they follow from the solved locks. A door is open once its scene is
/// unlocked and its <see cref="DoorOpenCondition"/> is met, and a scene is unlocked when it is the start scene or an
/// open door leads to it. The frontend's play/progress.ts follows the same rules.
/// </summary>
public sealed class RoomProgress
{
    private readonly IReadOnlySet<Guid> _solvedLockIds;
    private readonly Dictionary<Guid, RoomObject> _objects;
    private readonly HashSet<Guid> _unlockedSceneIds = [];
    private readonly HashSet<Guid> _openDoorwayIds = [];

    public RoomProgress(EscapeRoom room, IReadOnlySet<Guid> solvedLockIds)
    {
        _solvedLockIds = solvedLockIds;
        _objects = room.Objects.ToDictionary(o => o.Id);

        if (room.StartScene is { } start)
        {
            _unlockedSceneIds.Add(start.Id);
        }

        // Opening a door unlocks its target scene, whose doors may then open too, until nothing changes.
        var locksByScene = room.Objects.OfType<Lock>().ToLookup(l => l.SceneId, l => l.Id);
        var doorways = room.Objects.OfType<Doorway>().ToList();
        bool changed;
        do
        {
            changed = false;
            foreach (var doorway in doorways.Where(d => !_openDoorwayIds.Contains(d.Id) && _unlockedSceneIds.Contains(d.SceneId)))
            {
                if (!IsConditionMet(doorway, locksByScene[doorway.SceneId]))
                {
                    continue;
                }

                _openDoorwayIds.Add(doorway.Id);
                changed = true;
                if (doorway is Door { TargetSceneId: { } target })
                {
                    _unlockedSceneIds.Add(target);
                }
            }
        }
        while (changed);
    }

    public static RoomProgress For(EscapeRoom room, Team team) => new(room, team.SolvedLockIds);

    /// <summary>The doors and exit doors that are open, in no particular order.</summary>
    public IReadOnlySet<Guid> OpenDoorwayIds => _openDoorwayIds;

    public bool IsDoorwayOpen(Doorway doorway) => _openDoorwayIds.Contains(doorway.Id);

    /// <summary>A lock is completed when solved, a door when open. Letters and text boxes never complete anything.</summary>
    public bool IsCompleted(RoomObject roomObject) => roomObject switch
    {
        Lock => _solvedLockIds.Contains(roomObject.Id),
        Doorway => _openDoorwayIds.Contains(roomObject.Id),
        _ => false,
    };

    /// <summary>
    /// The object is visible and can be interacted with: its scene is unlocked and the object it is unlocked by
    /// (if any) is completed. Doors are always visible in an unlocked scene, open or not.
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

    /// <summary>The team can walk through the exit door: it is open.</summary>
    public bool CanOpenExitDoor(ExitDoor exitDoor) => IsDoorwayOpen(exitDoor);

    /// <summary>The start scene, and every scene an open door leads to.</summary>
    public bool IsSceneUnlocked(Guid sceneId) => _unlockedSceneIds.Contains(sceneId);

    private bool IsConditionMet(Doorway doorway, IEnumerable<Guid> locksInScene) => doorway.OpenCondition switch
    {
        DoorOpenCondition.AllInScene => locksInScene.All(_solvedLockIds.Contains),

        // A lock that is no longer in the room can never be solved, so the door stays shut (and Validate says why).
        DoorOpenCondition.SelectedLocks => doorway.RequiredLockIds.Count > 0
            && doorway.RequiredLockIds.All(id => _objects.ContainsKey(id) && _solvedLockIds.Contains(id)),
        _ => false,
    };
}
