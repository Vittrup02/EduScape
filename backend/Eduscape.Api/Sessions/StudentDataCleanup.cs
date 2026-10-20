using Eduscape.Domain.Sessions;
using Eduscape.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Eduscape.Api.Sessions;

/// <summary>
/// Erases students' data (nicknames, device tokens, scenes) once a session has been closed for
/// <see cref="SessionOptions.StudentDataRetention"/>. The teams' results stay, without names, for the teacher's history.
/// Sessions that expired without being closed are closed first.
/// </summary>
public sealed class StudentDataCleanup(EduscapeDbContext db, TimeProvider timeProvider, IOptions<SessionOptions> options)
{
    /// <summary>Runs one cleanup. Returns the number of sessions whose student data was erased.</summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await ExpiredSessions.CloseAsync(db, now, cancellationToken);

        var cutoff = now - options.Value.StudentDataRetention;
        var sessions = await db.Sessions
            .Include(s => s.Teams).ThenInclude(t => t.Participants)
            .AsSplitQuery()
            .Where(s => s.Status == SessionStatus.Closed && s.ClosedAt <= cutoff
                && s.Teams.Any(t => t.Participants.Any()))
            .ToListAsync(cancellationToken);

        foreach (var session in sessions)
        {
            session.EraseStudentData();
        }

        await db.SaveChangesAsync(cancellationToken);
        return sessions.Count;
    }
}

/// <summary>
/// Runs <see cref="StudentDataCleanup"/> every <see cref="SessionOptions.CleanupInterval"/>. The first run waits one
/// interval, so startup stays light. On Render's free tier it runs whenever the instance is awake.
/// </summary>
public sealed class StudentDataCleanupService(
    IServiceScopeFactory scopes,
    TimeProvider timeProvider,
    IOptions<SessionOptions> options,
    ILogger<StudentDataCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.CleanupInterval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var erased = await scope.ServiceProvider.GetRequiredService<StudentDataCleanup>().RunOnceAsync(stoppingToken);
                if (erased > 0)
                {
                    logger.LogInformation("Erased student data of {Count} closed sessions", erased);
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // E.g. the database is briefly unreachable; the next run tries again.
                logger.LogError(ex, "Student data cleanup failed");
            }
        }
    }
}
