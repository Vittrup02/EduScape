using Eduscape.Domain.Rooms;
using Eduscape.Domain.Sessions;

namespace Eduscape.Tests.Domain;

public class RoomProgressTests
{
    private static readonly Position Center = new(50, 50);
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);

    // Scene 1: letter, lock A, lock B (after A), door to scene 2 (after B).
    // Scene 2: lock C, exit door (after C).
    private readonly EscapeRoom _room = new("Demo", Subject.Mathematics, GradeLevel.Grade7);
    private readonly Scene _scene1;
    private readonly Scene _scene2;
    private readonly Letter _letter;
    private readonly Lock _lockA;
    private readonly Lock _lockB;
    private readonly Door _door;
    private readonly Lock _lockC;
    private readonly ExitDoor _exit;

    public RoomProgressTests()
    {
        _scene1 = _room.AddScene("Klasselokalet", "s1.jpg");
        _scene2 = _room.AddScene("Biblioteket", "s2.jpg");

        _letter = _scene1.AddObject(new Letter(Center, "letter.png", "Læs side 42"));
        _lockA = _scene1.AddObject(NewLock());
        _lockB = _scene1.AddObject(NewLock());
        _lockB.SetUnlockedBy(_lockA);
        _door = _scene1.AddObject(new Door(Center, "door.png", null, _scene2.Id));
        _door.SetUnlockedBy(_lockB);

        _lockC = _scene2.AddObject(NewLock());
        _exit = _scene2.AddObject(new ExitDoor(Center, "exit.png", null));
        _exit.SetUnlockedBy(_lockC);
    }

    private static Lock NewLock() => new(Center, "lock.png", "lock-open.png", "?", AnswerType.Code, "1");

    private RoomProgress Progress(Guid[]? solved = null, Guid[]? opened = null) =>
        new(_room, (solved ?? []).ToHashSet(), (opened ?? []).ToHashSet());

    [Fact]
    public void ObjectsWithoutUnlockedBy_AreAvailableFromStart()
    {
        var progress = Progress();

        Assert.True(progress.IsAvailable(_letter));
        Assert.True(progress.IsAvailable(_lockA));
    }

    [Fact]
    public void ObjectWithUnlockedBy_IsHiddenUntilSourceIsSolved()
    {
        Assert.False(Progress().IsAvailable(_lockB));
        Assert.True(Progress(solved: [_lockA.Id]).IsAvailable(_lockB));
    }

    [Fact]
    public void Chain_UnlocksStepByStep()
    {
        Assert.False(Progress(solved: [_lockA.Id]).IsAvailable(_door));
        Assert.True(Progress(solved: [_lockA.Id, _lockB.Id]).IsAvailable(_door));
    }

    [Fact]
    public void ObjectUnlockedByDoor_IsAvailableOnceDoorIsOpened()
    {
        var behindDoor = _scene2.AddObject(new Letter(Center, "letter.png", "Godt gået"));
        behindDoor.SetUnlockedBy(_door);

        Assert.False(Progress(solved: [_lockA.Id, _lockB.Id]).IsAvailable(behindDoor));
        Assert.True(Progress(solved: [_lockA.Id, _lockB.Id], opened: [_door.Id]).IsAvailable(behindDoor));
    }

    [Fact]
    public void ObjectsInLockedScene_AreNotAvailable()
    {
        // Lock C has no unlockedBy, but its scene is only reachable through the door.
        Assert.False(Progress().IsAvailable(_lockC));
        Assert.True(Progress(opened: [_door.Id]).IsAvailable(_lockC));
    }

    [Fact]
    public void IsCompleted_CoversSolvedLocksAndOpenedDoors()
    {
        var progress = Progress(solved: [_lockA.Id], opened: [_door.Id]);

        Assert.True(progress.IsCompleted(_lockA));
        Assert.True(progress.IsCompleted(_door));
        Assert.False(progress.IsCompleted(_lockB));
        Assert.False(progress.IsCompleted(_letter));
    }

    [Fact]
    public void Scenes_StartSceneAndScenesBehindOpenedDoorsAreUnlocked()
    {
        Assert.True(Progress().IsSceneUnlocked(_scene1.Id));
        Assert.False(Progress().IsSceneUnlocked(_scene2.Id));
        Assert.True(Progress(opened: [_door.Id]).IsSceneUnlocked(_scene2.Id));
    }

    [Fact]
    public void ExitDoor_RequiresEveryLockInTheRoom()
    {
        // Exit only depends on lock C, but every lock in the room must be solved.
        Assert.False(Progress(solved: [_lockC.Id], opened: [_door.Id]).CanOpenExitDoor(_exit));
        Assert.True(Progress(solved: [_lockA.Id, _lockB.Id, _lockC.Id], opened: [_door.Id]).CanOpenExitDoor(_exit));
    }

    [Fact]
    public void ExitDoor_RequiresItsOwnUnlockedBy()
    {
        var hidden = new ExitDoor(Center, "exit.png", null);
        _scene1.AddObject(hidden);
        hidden.SetUnlockedBy(_door);

        Assert.False(Progress(solved: [_lockA.Id, _lockB.Id, _lockC.Id]).CanOpenExitDoor(hidden));
    }

    [Fact]
    public void For_UsesTeamProgress()
    {
        var team = new Session(_room.Id, Guid.NewGuid(), "123456", SessionMode.Team, Now, TimeSpan.FromHours(12)).AddTeam(TeamColor.Green);
        team.TrySolveLock(_lockA.Id, Now);

        Assert.True(RoomProgress.For(_room, team).IsAvailable(_lockB));
    }
}
