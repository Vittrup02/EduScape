namespace Eduscape.Domain.Rooms;

/// <summary>When a door or exit door opens by itself, for the whole team.</summary>
public enum DoorOpenCondition
{
    /// <summary>Once every lock in the door's own scene is solved; a scene without locks has its doors open.</summary>
    AllInScene,

    /// <summary>Once the locks the teacher chose (<see cref="Doorway.RequiredLockIds"/>) are solved.</summary>
    SelectedLocks,
}
