using Eduscape.Domain.Classes;
using Eduscape.Domain.Sessions;
using Eduscape.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Eduscape.Api.Sessions;

public sealed record StartSessionRequest(Guid RoomId, string? ClassName, SessionMode Mode, int TeamCount);

/// <summary>
/// Starts a session: finds or creates the class by name and gives the session a PIN that no other active session has.
/// </summary>
public sealed class SessionStarter(
    EduscapeDbContext db,
    IPinGenerator pinGenerator,
    TimeProvider timeProvider,
    IOptions<SessionOptions> options)
{
    /// <summary>With at most a few hundred active sessions among a million PINs, a collision is already rare.</summary>
    public const int MaxPinDraws = 10;

    /// <summary>Retries when another session took the same PIN between the check and the insert.</summary>
    public const int MaxSaveAttempts = 3;

    /// <summary>The new session's id, or null if the room is neither the teacher's own nor a template.</summary>
    public async Task<Guid?> StartAsync(Guid teacherId, StartSessionRequest request, CancellationToken cancellationToken)
    {
        var roomIsAvailable = await db.EscapeRooms.AnyAsync(
            r => r.Id == request.RoomId && (r.OwnerTeacherId == teacherId || r.IsTemplate),
            cancellationToken);
        if (!roomIsAvailable)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        await CloseExpiredSessionsAsync(now, cancellationToken);

        for (var attempt = 1; ; attempt++)
        {
            var schoolClass = await FindOrAddClassAsync(teacherId, request.ClassName, cancellationToken);
            var pin = await DrawFreePinAsync(cancellationToken);
            var session = Session.Start(
                request.RoomId, schoolClass.Id, pin, request.Mode, request.TeamCount, now, options.Value.Lifetime);
            db.Sessions.Add(session);

            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return session.Id;
            }
            catch (DbUpdateException ex)
                when (attempt < MaxSaveAttempts && DatabaseErrors.IsUniqueViolation(ex, DatabaseErrors.ActiveSessionPinIndex))
            {
                // Start over with a fresh PIN; the class is looked up (or added) again too.
                db.ChangeTracker.Clear();
            }
        }
    }

    /// <summary>
    /// Closes every session whose PIN has expired, as of its expiry time (like <see cref="Session.CloseIfExpired"/>),
    /// which releases the PINs for reuse.
    /// </summary>
    private Task<int> CloseExpiredSessionsAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Sessions
            .Where(s => s.Status != SessionStatus.Closed && s.ExpiresAt <= now)
            .ExecuteUpdateAsync(
                update => update
                    .SetProperty(s => s.Status, SessionStatus.Closed)
                    .SetProperty(s => s.ClosedAt, s => s.ExpiresAt),
                cancellationToken);

    /// <summary>A class is just a label: "9.A" and " 9.a " are the same class of the same teacher.</summary>
    private async Task<Class> FindOrAddClassAsync(Guid teacherId, string? name, CancellationToken cancellationToken)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        var lower = trimmed.ToLowerInvariant();
        var existing = await db.Classes
            .Where(c => c.OwnerTeacherId == teacherId && c.Name.ToLower() == lower)
            .OrderBy(c => c.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        // The constructor validates the name (required, max length).
        var schoolClass = new Class(teacherId, trimmed);
        db.Classes.Add(schoolClass);
        return schoolClass;
    }

    private async Task<string> DrawFreePinAsync(CancellationToken cancellationToken)
    {
        for (var draw = 0; draw < MaxPinDraws; draw++)
        {
            var pin = pinGenerator.Next();
            var taken = await db.Sessions.AnyAsync(s => s.Pin == pin && s.Status != SessionStatus.Closed, cancellationToken);
            if (!taken)
            {
                return pin;
            }
        }

        throw new InvalidOperationException($"No free PIN found in {MaxPinDraws} draws.");
    }
}
