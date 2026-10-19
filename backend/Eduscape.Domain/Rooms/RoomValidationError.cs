namespace Eduscape.Domain.Rooms;

/// <summary>A problem that makes a room unplayable, e.g. for highlighting in the editor.</summary>
/// <param name="Code">Error code from <see cref="Common.DomainErrors"/>, translated in the frontend.</param>
/// <param name="ObjectId">The offending object, or null for room-level errors.</param>
public sealed record RoomValidationError(string Code, Guid? ObjectId);
