namespace Eduscape.Domain.Rooms;

/// <summary>A problem that makes a room unplayable, e.g. for highlighting in the editor.</summary>
/// <param name="Code">Error code from <see cref="Common.DomainErrors"/>, translated in the frontend.</param>
/// <param name="ObjectId">The offending object, or null for room-level errors.</param>
/// <param name="StepIndex">For a lock's step, which step (0-based); null otherwise.</param>
public sealed record RoomValidationError(string Code, Guid? ObjectId, int? StepIndex = null);
