using Eduscape.Domain.Classes;
using Eduscape.Domain.Rooms;
using Eduscape.Domain.Sessions;
using Eduscape.Infrastructure.Identity;
using Eduscape.Infrastructure.Persistence;
using Eduscape.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Eduscape.Tests.Infrastructure;

public class PostgresIntegrationTests(PostgresDatabaseFixture database) : IClassFixture<PostgresDatabaseFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);

    [PostgresFact]
    public async Task DemoRoom_IsSeededOnceWithAllObjectTypes()
    {
        // The fixture already migrated (and seeded) once; migrating again must not duplicate the room.
        await using (var context = database.CreateContext())
        {
            await context.Database.MigrateAsync();
        }

        await using var db = database.CreateContext();
        Assert.Equal(1, await db.EscapeRooms.CountAsync(r => r.Id == DemoRoomSeeder.DemoRoomId));

        var room = await LoadRoomAsync(db, DemoRoomSeeder.DemoRoomId);
        Assert.Empty(room.Validate());
        Assert.Equal(["Klasselokalet", "Biblioteket"], room.Scenes.OrderBy(s => s.SortOrder).Select(s => s.Name));
        Assert.Equal("Klasselokalet", room.StartScene?.Name);
        Assert.Equal(Subject.Mathematics, room.Subject);
        Assert.Equal(GradeLevel.Grade7, room.GradeLevel);

        var locks = room.Objects.OfType<Lock>().ToList();
        Assert.Equal(3, locks.Count);
        Assert.Single(room.Objects.OfType<Letter>());
        Assert.Single(room.Objects.OfType<Door>());
        Assert.Single(room.Objects.OfType<ExitDoor>());

        var numeric = Assert.Single(locks, l => l.AnswerType == AnswerType.Text);
        Assert.Equal(0.01m, numeric.Tolerance);
        Assert.NotNull(numeric.UnlockedById);
        Assert.Equal(new Position(65, 50), numeric.Position);

        var multipleChoice = Assert.Single(locks, l => l.AnswerType == AnswerType.MultipleChoice);
        Assert.Equal(["9", "15", "17", "21"], multipleChoice.Options);
    }

    [PostgresFact]
    public async Task SessionProgress_RoundTrips()
    {
        var lockId = await DemoLockIdAsync();
        var sessionId = await CreateSessionAsync(RandomPin(), (team, now) =>
        {
            team.AddParticipant("Ida", $"device-{Guid.NewGuid():N}", now);
            team.RegisterWrongAttempt(lockId);
            team.TrySolveLock(lockId, now);
        });

        await using var db = database.CreateContext();
        var session = await db.Sessions
            .Include(s => s.Teams).ThenInclude(t => t.LockProgress)
            .Include(s => s.Teams).ThenInclude(t => t.Participants)
            .SingleAsync(s => s.Id == sessionId);

        var team = Assert.Single(session.Teams);
        Assert.Equal(TeamColor.Red, team.Color);
        Assert.True(team.IsLockSolved(lockId));
        Assert.Equal(1, team.WrongAttempts(lockId));
        Assert.Equal("Ida", Assert.Single(team.Participants).Nickname);
    }

    [PostgresFact]
    public async Task SameLockSolvedConcurrently_IsRegisteredOnce()
    {
        var lockId = await DemoLockIdAsync();
        var sessionId = await CreateSessionAsync(RandomPin());

        // Two students on the same team answer at the same time, each through their own request/context.
        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        var teamInFirst = await first.Teams.Include(t => t.LockProgress).SingleAsync(t => t.SessionId == sessionId);
        var teamInSecond = await second.Teams.Include(t => t.LockProgress).SingleAsync(t => t.SessionId == sessionId);

        Assert.True(teamInFirst.TrySolveLock(lockId, Now));
        Assert.True(teamInSecond.TrySolveLock(lockId, Now));
        await first.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(ex.InnerException).SqlState);

        await using var db = database.CreateContext();
        var registrations = await db.Teams
            .Where(t => t.SessionId == sessionId)
            .SelectMany(t => t.LockProgress)
            .CountAsync(p => p.LockId == lockId);
        Assert.Equal(1, registrations);
    }

    [PostgresFact]
    public async Task Pin_IsUniqueAmongActiveSessionsOnly()
    {
        var pin = RandomPin();
        var firstId = await CreateSessionAsync(pin);

        await Assert.ThrowsAsync<DbUpdateException>(() => CreateSessionAsync(pin));

        await using (var db = database.CreateContext())
        {
            var first = await db.Sessions.SingleAsync(s => s.Id == firstId);
            first.Close(Now);
            await db.SaveChangesAsync();
        }

        // Once the first session is closed its PIN can be reused.
        await CreateSessionAsync(pin);
    }

    private static async Task<EscapeRoom> LoadRoomAsync(EduscapeDbContext db, Guid roomId) =>
        await db.EscapeRooms
            .Include(r => r.Scenes).ThenInclude(s => s.Objects)
            .AsSplitQuery()
            .SingleAsync(r => r.Id == roomId);

    private async Task<Guid> DemoLockIdAsync()
    {
        await using var db = database.CreateContext();
        var room = await LoadRoomAsync(db, DemoRoomSeeder.DemoRoomId);
        return room.Objects.OfType<Lock>().First().Id;
    }

    private async Task<Guid> CreateSessionAsync(string pin, Action<Team, DateTimeOffset>? arrange = null)
    {
        await using var db = database.CreateContext();
        var teacher = new Teacher($"teacher-{Guid.NewGuid():N}@example.com");
        var schoolClass = new Class(teacher.Id, "9.A");
        var session = new Session(DemoRoomSeeder.DemoRoomId, schoolClass.Id, pin, SessionMode.Team, Now);
        var team = session.AddTeam(TeamColor.Red);
        arrange?.Invoke(team, Now);

        db.AddRange(teacher, schoolClass, session);
        await db.SaveChangesAsync();
        return session.Id;
    }

    private static string RandomPin() => Random.Shared.Next(0, 1_000_000).ToString("D6");
}
