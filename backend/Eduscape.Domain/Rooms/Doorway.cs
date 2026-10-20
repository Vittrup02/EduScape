using Eduscape.Domain.Common;

namespace Eduscape.Domain.Rooms;

/// <summary>
/// A door or the exit door. It is always visible, closed until its <see cref="OpenCondition"/> is met, and then opens
/// by itself for the whole team. Students walk through an open door themselves; it never moves them on its own.
/// A doorway never waits for anything with unlockedBy: its open condition is what it waits for.
/// </summary>
public abstract class Doorway : RoomObject
{
    private Guid[] _requiredLockIds = [];

    protected Doorway(Position position, string graphicKey, string? unlockedGraphicKey, Guid? id)
        : base(position, graphicKey, unlockedGraphicKey, id)
    {
    }

    // For EF Core
    protected Doorway()
    {
    }

    public DoorOpenCondition OpenCondition { get; private set; } = DoorOpenCondition.AllInScene;

    /// <summary>The locks that open it with <see cref="DoorOpenCondition.SelectedLocks"/>; empty otherwise.</summary>
    public IReadOnlyList<Guid> RequiredLockIds => _requiredLockIds;

    /// <summary>
    /// Locks that are not in the room (or a selection without any lock) are reported by <see cref="EscapeRoom.Validate"/>.
    /// </summary>
    public void SetOpenCondition(DoorOpenCondition condition, IEnumerable<Guid>? requiredLockIds = null)
    {
        if (!Enum.IsDefined(condition))
        {
            throw new DomainException(DomainErrors.DoorConditionInvalid);
        }

        OpenCondition = condition;
        _requiredLockIds = condition == DoorOpenCondition.SelectedLocks ? [.. (requiredLockIds ?? []).Distinct()] : [];
    }

    /// <summary>Forgets a lock that was removed from the room.</summary>
    internal void ForgetLock(Guid lockId) => _requiredLockIds = [.. _requiredLockIds.Where(id => id != lockId)];
}
