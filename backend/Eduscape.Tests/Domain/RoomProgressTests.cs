using Eduscape.Domain.Common;
using Eduscape.Domain.Rooms;
using Eduscape.Domain.Sessions;

namespace Eduscape.Tests.Domain;

public class RoomProgressTests
{
    private static readonly Position Center = new(50, 50);
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);

    // Scene 1: letter, lock A, lock B (after A), door to scene 2 (opens once every lock in scene 1 is solved).
    // Scene 2: lock C, exit door (opens once C is solved).
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

        _lockC = _scene2.AddObject(NewLock());
        _exit = _scene2.AddObject(new ExitDoor(Center, "exit.png", null));
    }

    private static Lock NewLock() => new(Center, "lock.png", "lock-open.png", "?", AnswerType.Code, "1");

    private RoomProgress Progress(params Lock[] solved) => new(_room, solved.Select(l => l.Id).ToHashSet());

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
        Assert.True(Progress(_lockA).IsAvailable(_lockB));
    }

    [Fact]
    public void Doors_AreAlwaysVisible_ButOpenOnlyWhenEveryLockInTheirSceneIsSolved()
    {
        Assert.True(Progress().IsAvailable(_door));
        Assert.False(Progress(_lockA).IsDoorwayOpen(_door));
        Assert.True(Progress(_lockA, _lockB).IsDoorwayOpen(_door));
    }

    [Fact]
    public void DoorWithSelectedLocks_OpensWhenThoseAreSolved_EvenIfOthersAreNot()
    {
        _door.SetOpenCondition(DoorOpenCondition.SelectedLocks, [_lockA.Id]);

        Assert.False(Progress().IsDoorwayOpen(_door));
        Assert.True(Progress(_lockA).IsDoorwayOpen(_door));
    }

    [Fact]
    public void DoorWithSelectedLocks_NeedsAllOfThem()
    {
        _door.SetOpenCondition(DoorOpenCondition.SelectedLocks, [_lockA.Id, _lockC.Id]);

        Assert.False(Progress(_lockA, _lockB).IsDoorwayOpen(_door));
        Assert.True(Progress(_lockA, _lockC).IsDoorwayOpen(_door));
    }

    [Fact]
    public void DoorWithSelectedLocks_ButNoneChosen_NeverOpens()
    {
        _door.SetOpenCondition(DoorOpenCondition.SelectedLocks, []);

        Assert.False(Progress(_lockA, _lockB, _lockC).IsDoorwayOpen(_door));
    }

    [Fact]
    public void DoorInASceneWithoutLocks_IsOpenFromTheStart()
    {
        var hall = _room.AddScene("Gangen", "s3.jpg");
        var toHall = _scene2.AddObject(new Door(Center, "door.png", null, hall.Id));
        var back = hall.AddObject(new Door(Center, "door.png", null, _scene1.Id));

        var progress = Progress(_lockA, _lockB, _lockC);

        Assert.True(progress.IsDoorwayOpen(toHall));
        Assert.True(progress.IsDoorwayOpen(back));
    }

    [Fact]
    public void DoorInALockedScene_StaysShut_EvenIfItsConditionIsMet()
    {
        // Lock C is solved (e.g. by a preview), but scene 2 is not open yet, so neither is its exit door.
        Assert.False(Progress(_lockC).IsDoorwayOpen(_exit));
    }

    [Fact]
    public void ObjectUnlockedByDoor_IsAvailableOnceTheDoorIsOpen()
    {
        var behindDoor = _scene2.AddObject(new Letter(Center, "letter.png", "Godt gået"));
        behindDoor.SetUnlockedBy(_door);

        Assert.False(Progress(_lockA).IsAvailable(behindDoor));
        Assert.True(Progress(_lockA, _lockB).IsAvailable(behindDoor));
    }

    [Fact]
    public void ObjectsInLockedScene_AreNotAvailable()
    {
        // Lock C has no unlockedBy, but its scene is only reachable through the door.
        Assert.False(Progress().IsAvailable(_lockC));
        Assert.True(Progress(_lockA, _lockB).IsAvailable(_lockC));
    }

    [Fact]
    public void IsCompleted_CoversSolvedLocksAndOpenDoors()
    {
        var progress = Progress(_lockA, _lockB);

        Assert.True(progress.IsCompleted(_lockA));
        Assert.True(progress.IsCompleted(_door));
        Assert.False(progress.IsCompleted(_lockC));
        Assert.False(progress.IsCompleted(_letter));
    }

    [Fact]
    public void Scenes_StartSceneAndScenesBehindOpenDoorsAreUnlocked()
    {
        Assert.True(Progress().IsSceneUnlocked(_scene1.Id));
        Assert.False(Progress().IsSceneUnlocked(_scene2.Id));
        Assert.True(Progress(_lockA, _lockB).IsSceneUnlocked(_scene2.Id));
    }

    [Fact]
    public void ExitDoor_OpensWithItsSceneOnly_NotTheWholeRoom()
    {
        // Like any door: the exit door's scene has only lock C, so locks elsewhere don't matter once the scene is open.
        _door.SetOpenCondition(DoorOpenCondition.SelectedLocks, [_lockA.Id]);

        Assert.False(Progress(_lockA).CanOpenExitDoor(_exit));
        Assert.True(Progress(_lockA, _lockC).CanOpenExitDoor(_exit));
    }

    [Fact]
    public void Doors_CannotWaitWithUnlockedBy()
    {
        var error = Assert.Throws<DomainException>(() => _door.SetUnlockedBy(_lockA));

        Assert.Equal(DomainErrors.DoorCannotWait, error.Code);
        Assert.Null(_door.UnlockedById);
    }

    [Fact]
    public void For_UsesTeamProgress()
    {
        var team = new Session(_room.Id, Guid.NewGuid(), "123456", SessionMode.Team, Now, TimeSpan.FromHours(12)).AddTeam(TeamColor.Green);
        team.TrySolveLock(_lockA.Id, Now);

        Assert.True(RoomProgress.For(_room, team).IsAvailable(_lockB));
    }
}
