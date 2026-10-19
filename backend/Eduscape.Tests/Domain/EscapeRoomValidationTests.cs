using Eduscape.Domain.Common;
using Eduscape.Domain.Rooms;

namespace Eduscape.Tests.Domain;

public class EscapeRoomValidationTests
{
    private static readonly Position Center = new(50, 50);

    private static Lock NewLock() => new(Center, "lock.png", null, "?", AnswerType.Code, "1");

    private static (EscapeRoom Room, Scene Scene) NewRoom()
    {
        var room = new EscapeRoom("Rum", Subject.Danish, GradeLevel.Grade5);
        return (room, room.AddScene("Scene", "s.jpg"));
    }

    [Fact]
    public void ValidRoom_HasNoErrors()
    {
        var (room, scene) = NewRoom();
        var second = room.AddScene("Anden", "s2.jpg");
        var lockA = scene.AddObject(NewLock());
        var door = scene.AddObject(new Door(Center, "door.png", null, second.Id));
        door.SetUnlockedBy(lockA);
        second.AddObject(new ExitDoor(Center, "exit.png", null)).SetUnlockedBy(door);

        Assert.Empty(room.Validate());
    }

    [Fact]
    public void RoomWithoutScenes_ReportsMissingStartScene()
    {
        var room = new EscapeRoom("Tomt", Subject.Danish, GradeLevel.Grade5);

        var error = Assert.Single(room.Validate());

        Assert.Equal(new RoomValidationError(DomainErrors.StartSceneMissing, null), error);
    }

    [Fact]
    public void UnlockedByUnknownObject_IsReported()
    {
        var (room, scene) = NewRoom();
        var (_, otherScene) = NewRoom();
        var foreignLock = otherScene.AddObject(NewLock());
        var target = scene.AddObject(NewLock());
        target.SetUnlockedBy(foreignLock); // object from another room

        var error = Assert.Single(room.Validate());

        Assert.Equal(new RoomValidationError(DomainErrors.UnlockedByNotFound, target.Id), error);
    }

    [Fact]
    public void UnlockedBySelf_IsReported()
    {
        var (room, scene) = NewRoom();
        var target = scene.AddObject(NewLock());
        target.SetUnlockedBy(target);

        var error = Assert.Single(room.Validate());

        Assert.Equal(new RoomValidationError(DomainErrors.UnlockedBySelf, target.Id), error);
    }

    [Fact]
    public void UnlockedByLetterOrExitDoor_IsReported()
    {
        var (room, scene) = NewRoom();
        var letter = scene.AddObject(new Letter(Center, "letter.png", "Ledetråd"));
        var exit = scene.AddObject(new ExitDoor(Center, "exit.png", null));
        var afterLetter = scene.AddObject(NewLock());
        var afterExit = scene.AddObject(NewLock());
        afterLetter.SetUnlockedBy(letter);
        afterExit.SetUnlockedBy(exit);

        var errors = room.Validate();

        Assert.Equal(
            [
                new RoomValidationError(DomainErrors.UnlockedByInvalidTarget, afterLetter.Id),
                new RoomValidationError(DomainErrors.UnlockedByInvalidTarget, afterExit.Id),
            ],
            errors);
    }

    [Fact]
    public void Cycle_IsReportedForEveryObjectInIt()
    {
        var (room, scene) = NewRoom();
        var a = scene.AddObject(NewLock());
        var b = scene.AddObject(NewLock());
        var c = scene.AddObject(NewLock());
        var outside = scene.AddObject(NewLock());
        a.SetUnlockedBy(b);
        b.SetUnlockedBy(c);
        c.SetUnlockedBy(a);
        outside.SetUnlockedBy(a); // depends on the cycle but is not part of it

        var errors = room.Validate();

        Assert.All(errors, e => Assert.Equal(DomainErrors.UnlockedByCycle, e.Code));
        Assert.Equal(new[] { a.Id, b.Id, c.Id }.Order(), errors.Select(e => e.ObjectId!.Value).Order());
    }

    [Fact]
    public void DoorWithoutTargetInRoom_IsReported()
    {
        var (room, scene) = NewRoom();
        var (_, otherRoomScene) = NewRoom();
        var noTarget = scene.AddObject(new Door(Center, "door.png", null, otherRoomScene.Id));
        var cleared = scene.AddObject(new Door(Center, "door.png", null, scene.Id));
        cleared.SetTarget(null);

        var errors = room.Validate();

        Assert.Equal(
            [
                new RoomValidationError(DomainErrors.DoorTargetMissing, noTarget.Id),
                new RoomValidationError(DomainErrors.DoorTargetMissing, cleared.Id),
            ],
            errors);
    }
}
