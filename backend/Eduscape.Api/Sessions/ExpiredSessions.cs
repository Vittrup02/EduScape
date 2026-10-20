using Eduscape.Domain.Sessions;
using Eduscape.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Eduscape.Api.Sessions;

internal static class ExpiredSessions
{
    /// <summary>
    /// Closes every session whose PIN has expired, as of its expiry time (like <see cref="Session.CloseIfExpired"/>),
    /// which releases the PINs for reuse.
    /// </summary>
    public static Task<int> CloseAsync(EduscapeDbContext db, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Sessions
            .Where(s => s.Status != SessionStatus.Closed && s.ExpiresAt <= now)
            .ExecuteUpdateAsync(
                update => update
                    .SetProperty(s => s.Status, SessionStatus.Closed)
                    .SetProperty(s => s.ClosedAt, s => s.ExpiresAt),
                cancellationToken);
}
