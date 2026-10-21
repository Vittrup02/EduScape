using Eduscape.Domain.Rooms;
using Eduscape.Infrastructure.Persistence.Seeding;

namespace Eduscape.Tests.Infrastructure;

public class DemoRoomSeederTests
{
    [Fact]
    public void DemoRoom_IsValidAndPlayable()
    {
        var room = DemoRoomSeeder.BuildDemoRoom();

        Assert.Empty(room.Validate());
        Assert.Equal(DemoRoomSeeder.DemoRoomId, room.Id);
        Assert.Equal(2, room.Scenes.Count);
        Assert.Equal(3, room.Objects.OfType<Lock>().Count());
        Assert.Single(room.Objects.OfType<Letter>());
        Assert.Single(room.Objects.OfType<Door>());
        Assert.Single(room.Objects.OfType<ExitDoor>());
    }

    [Fact]
    public void DemoRoom_UsesOnlyBuiltInAssets()
    {
        var room = DemoRoomSeeder.BuildDemoRoom();

        Assert.All(room.Scenes, s => Assert.StartsWith("builtin:", s.BackgroundImageKey));
        Assert.All(room.Objects, o => Assert.StartsWith("builtin:", o.GraphicKey));

        // Built-in assets bring their own open state.
        Assert.All(room.Objects, o => Assert.Null(o.UnlockedGraphicKey));
    }

    [Fact]
    public void DemoRoom_CanBeCompletedInOrder()
    {
        var room = DemoRoomSeeder.BuildDemoRoom();
        var locks = room.Objects.OfType<Lock>().ToList();
        var door = room.Objects.OfType<Door>().Single();
        var exit = room.Objects.OfType<ExitDoor>().Single();
        var solved = new HashSet<Guid>();

        Assert.True(AnswerMatcher.IsCorrect(locks[0].Steps[0], AnswerSubmission.Typed("17")));
        solved.Add(locks[0].Id);
        Assert.True(new RoomProgress(room, solved).IsAvailable(locks[1]));
        Assert.False(new RoomProgress(room, solved).IsDoorwayOpen(door));

        Assert.True(AnswerMatcher.IsCorrect(locks[1].Steps[0], AnswerSubmission.Typed("3.333")));
        solved.Add(locks[1].Id);
        Assert.True(new RoomProgress(room, solved).IsDoorwayOpen(door));
        Assert.True(new RoomProgress(room, solved).IsAvailable(locks[2]));

        Assert.True(AnswerMatcher.IsCorrect(locks[2].Steps[0], AnswerSubmission.Typed("17")));
        solved.Add(locks[2].Id);
        Assert.True(new RoomProgress(room, solved).CanOpenExitDoor(exit));
    }

    [Fact]
    public void DemoRoom_WaitsOnlyBrieflyAfterAWrongAnswer() =>
        Assert.Equal(DemoRoomSeeder.DemoCooldownSeconds, DemoRoomSeeder.BuildDemoRoom().WrongAnswerCooldownSeconds);
}
