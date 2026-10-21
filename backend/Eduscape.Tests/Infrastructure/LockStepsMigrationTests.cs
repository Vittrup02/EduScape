using Eduscape.Domain.Rooms;
using Eduscape.Infrastructure;
using Eduscape.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Npgsql;

namespace Eduscape.Tests.Infrastructure;

/// <summary>
/// The migration that gave locks steps and doors an open condition moves existing rooms over: a database at the
/// migration before it gets rows in the old shape, is migrated, and is then read with today's model.
/// </summary>
public sealed class LockStepsMigrationTests : IAsyncLifetime
{
    private const string MigrationBefore = "20261020115414_AddTeacherDashboard";

    private ServiceProvider? _provider;

    [PostgresFact]
    public async Task ExistingLocksAndDoors_MoveToStepsAndOpenConditions()
    {
        await using var context = CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(MigrationBefore);

        var roomId = Guid.NewGuid();
        var hall = Guid.NewGuid();
        var library = Guid.NewGuid();
        var codeLock = Guid.NewGuid();
        var choiceLock = Guid.NewGuid();
        var doorAfterLock = Guid.NewGuid();
        var plainDoor = Guid.NewGuid();
        var exitAfterLock = Guid.NewGuid();
        await context.Database.ExecuteSqlAsync($"""
            INSERT INTO "EscapeRooms" ("Id", "Title", "Subject", "GradeLevel", "IsTemplate", "Revision")
            VALUES ({roomId}, 'Gammelt rum', 'Mathematics', 'Grade7', FALSE, 3);
            """);
        await context.Database.ExecuteSqlAsync($"""
            INSERT INTO "Scenes" ("Id", "EscapeRoomId", "Name", "BackgroundImageKey", "SortOrder", "IsStartScene")
            VALUES ({hall}, {roomId}, 'Gangen', 'builtin:classroom', 0, TRUE),
                   ({library}, {roomId}, 'Biblioteket', 'builtin:library', 1, FALSE);
            """);
        await context.Database.ExecuteSqlAsync($"""
            INSERT INTO "RoomObjects" ("Id", "SceneId", "ObjectType", "PositionX", "PositionY", "GraphicKey",
                                       "Question", "AnswerType", "CorrectAnswer", "Tolerance", "Hint", "Options")
            VALUES ({codeLock}, {hall}, 'Lock', 10, 10, 'builtin:code-lock', 'Kode?', 'Text', '3,14', 0.01, 'Cirkler', NULL),
                   ({choiceLock}, {library}, 'Lock', 20, 20, 'builtin:padlock', 'By?', 'MultipleChoice', 'Paris', NULL, NULL,
                    ARRAY['Paris', 'Rom']);
            """);
        await context.Database.ExecuteSqlAsync($"""
            INSERT INTO "RoomObjects" ("Id", "SceneId", "ObjectType", "PositionX", "PositionY", "GraphicKey",
                                       "UnlockedById", "TargetSceneId")
            VALUES ({doorAfterLock}, {hall}, 'Door', 80, 50, 'builtin:door', {codeLock}, {library}),
                   ({plainDoor}, {library}, 'Door', 10, 50, 'builtin:door', NULL, {hall}),
                   ({exitAfterLock}, {library}, 'ExitDoor', 90, 50, 'builtin:exit-door', {choiceLock}, NULL);
            """);

        await context.Database.MigrateAsync();
        context.ChangeTracker.Clear();

        var room = await context.EscapeRooms
            .Include(r => r.Scenes).ThenInclude(s => s.Objects)
            .AsSplitQuery()
            .SingleAsync(r => r.Id == roomId);
        var objects = room.Objects.ToDictionary(o => o.Id);

        var step = Assert.Single(Assert.IsType<Lock>(objects[codeLock]).Steps);
        Assert.Equal(("Kode?", AnswerType.Text, "3,14", 0.01m, "Cirkler"), (step.Question, step.AnswerType, step.CorrectAnswer, step.Tolerance, step.Hint));
        Assert.Empty(step.AcceptedAnswers);
        Assert.False(step.AcceptTypos);
        Assert.Equal(["Paris", "Rom"], Assert.Single(Assert.IsType<Lock>(objects[choiceLock]).Steps).Options);

        var door = Assert.IsType<Door>(objects[doorAfterLock]);
        Assert.Equal((DoorOpenCondition.SelectedLocks, (Guid?)null), (door.OpenCondition, door.UnlockedById));
        Assert.Equal([codeLock], door.RequiredLockIds);
        Assert.Equal(DoorOpenCondition.AllInScene, Assert.IsType<Door>(objects[plainDoor]).OpenCondition);
        var exit = Assert.IsType<ExitDoor>(objects[exitAfterLock]);
        Assert.Equal([choiceLock], exit.RequiredLockIds);
        Assert.Null(exit.UnlockedById);
        Assert.Equal(EscapeRoom.DefaultWrongAnswerCooldownSeconds, room.WrongAnswerCooldownSeconds);

        // The moved room plays: the door in the hall opens with the code lock, and so on.
        Assert.Empty(room.Validate());
    }

    public Task InitializeAsync()
    {
        if (PostgresDatabaseFixture.ServerConnectionString is null)
        {
            return Task.CompletedTask;
        }

        var connectionString = new NpgsqlConnectionStringBuilder(PostgresDatabaseFixture.ServerConnectionString)
        {
            Database = $"eduscape_test_{Guid.NewGuid():N}",
        }.ConnectionString;
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{DependencyInjection.ConnectionStringName}"] = connectionString,
            })
            .Build();

        // Production: the demo room is not seeded, so nothing but the test's own rows is in the database.
        _provider = new ServiceCollection()
            .AddInfrastructure(configuration, new HostingEnvironment { EnvironmentName = Environments.Production })
            .BuildServiceProvider();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_provider is null)
        {
            return;
        }

        NpgsqlConnection.ClearAllPools();
        await using (var context = CreateContext())
        {
            await context.Database.EnsureDeletedAsync();
        }

        await _provider.DisposeAsync();
    }

    private EduscapeDbContext CreateContext() =>
        new(_provider!.CreateScope().ServiceProvider.GetRequiredService<DbContextOptions<EduscapeDbContext>>());
}
