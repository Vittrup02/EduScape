using Eduscape.Domain.Common;
using Eduscape.Domain.Rooms;

namespace Eduscape.Tests.Domain;

public class EscapeRoomValidationTests
{
    private static readonly Position Center = new(50, 50);

    private static Lock NewLock() => new(Center, "lock.png", null, "?", AnswerType.Code, "1");

    /// <summary>A room with one scene and an exit door visible from the start, so each test adds only what it checks.</summary>
    private static (EscapeRoom Room, Scene Scene) NewRoom()
    {
        var (room, scene) = EmptyRoom();
        scene.AddObject(new ExitDoor(Center, "exit.png", null));
        return (room, scene);
    }

    private static (EscapeRoom Room, Scene Scene) EmptyRoom()
    {
        var room = new EscapeRoom("Rum", Subject.Danish, GradeLevel.Grade5);
        return (room, room.AddScene("Start", "s.jpg"));
    }

    [Fact]
    public void ValidRoom_HasNoErrors()
    {
        var (room, scene) = EmptyRoom();
        var second = room.AddScene("Anden", "s2.jpg");
        var lockA = scene.AddObject(NewLock());
        var door = scene.AddObject(new Door(Center, "door.png", null, second.Id));
        door.SetUnlockedBy(lockA);
        second.AddObject(new ExitDoor(Center, "exit.png", null)).SetUnlockedBy(door);

        Assert.Empty(room.Validate());
    }

    [Fact]
    public void RoomWithoutScenes_ReportsMissingStartSceneAndExitDoor()
    {
        var room = new EscapeRoom("Tomt", Subject.Danish, GradeLevel.Grade5);

        Assert.Equal(
            [
                new RoomValidationError(DomainErrors.StartSceneMissing, null),
                new RoomValidationError(DomainErrors.ExitDoorMissing, null),
            ],
            room.Validate());
    }

    [Fact]
    public void RoomWithoutExitDoor_IsReported()
    {
        var (room, scene) = EmptyRoom();
        scene.AddObject(NewLock());

        Assert.Equal([new RoomValidationError(DomainErrors.ExitDoorMissing, null)], room.Validate());
    }

    [Fact]
    public void LocksAndExitDoorInASceneNoDoorLeadsTo_AreUnreachable()
    {
        var (room, scene) = EmptyRoom();
        scene.AddObject(NewLock());
        var hidden = room.AddScene("Gemt", "s2.jpg");
        var hiddenLock = hidden.AddObject(NewLock());
        var exit = hidden.AddObject(new ExitDoor(Center, "exit.png", null));
        hidden.AddObject(new Letter(Center, "letter.png", "Et brev, ingen kan nå, spærrer ikke for noget."));

        Assert.Equal(
            [
                new RoomValidationError(DomainErrors.ObjectUnreachable, hiddenLock.Id),
                new RoomValidationError(DomainErrors.ObjectUnreachable, exit.Id),
            ],
            room.Validate());
    }

    [Fact]
    public void DoorWaitingForALockBehindItself_LeavesTheSceneUnreachable()
    {
        var (room, start) = EmptyRoom();
        var lab = room.AddScene("Lab", "s2.jpg");
        var door = start.AddObject(new Door(Center, "door.png", null, lab.Id));
        var labLock = lab.AddObject(NewLock());
        var exit = lab.AddObject(new ExitDoor(Center, "exit.png", null));
        door.SetUnlockedBy(labLock);

        Assert.Equal(
            [
                new RoomValidationError(DomainErrors.ObjectUnreachable, labLock.Id),
                new RoomValidationError(DomainErrors.ObjectUnreachable, exit.Id),
            ],
            room.Validate());
    }

    [Fact]
    public void SceneReachedThroughAChainOfLocksAndDoors_IsReachable()
    {
        var (room, start) = EmptyRoom();
        var lab = room.AddScene("Lab", "s2.jpg");
        var basement = room.AddScene("Kælder", "s3.jpg");
        var first = start.AddObject(NewLock());
        start.AddObject(new Door(Center, "door.png", null, lab.Id)).SetUnlockedBy(first);
        var labLock = lab.AddObject(NewLock());
        // Back in the start scene, a door appears once the lab's lock is solved.
        var toBasement = start.AddObject(new Door(Center, "door.png", null, basement.Id));
        toBasement.SetUnlockedBy(labLock);
        basement.AddObject(new ExitDoor(Center, "exit.png", null)).SetUnlockedBy(toBasement);

        Assert.Empty(room.Validate());
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

        var cycle = errors.Where(e => e.Code == DomainErrors.UnlockedByCycle);
        Assert.Equal(new[] { a.Id, b.Id, c.Id }.Order(), cycle.Select(e => e.ObjectId!.Value).Order());

        // The lock after the cycle can never appear either; the locks in the cycle already say why.
        Assert.Equal(
            new RoomValidationError(DomainErrors.ObjectUnreachable, outside.Id),
            Assert.Single(errors, e => e.Code != DomainErrors.UnlockedByCycle));
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

    [Fact]
    public void DoorToARemovedScene_IsReported()
    {
        var (room, scene) = NewRoom();
        var second = room.AddScene("Anden", "s2.jpg");
        var door = scene.AddObject(new Door(Center, "door.png", null, second.Id));

        room.RemoveScene(second.Id);

        Assert.Equal([new RoomValidationError(DomainErrors.DoorTargetMissing, door.Id)], room.Validate());
    }

    [Fact]
    public void NewDoorWithoutTarget_IsReported()
    {
        var (room, scene) = NewRoom();
        var door = scene.AddObject(new Door(Center, "door.png", null, targetSceneId: null));

        Assert.Equal([new RoomValidationError(DomainErrors.DoorTargetMissing, door.Id)], room.Validate());
    }

    [Fact]
    public void UnfinishedLockAndLetter_AreReported()
    {
        var (room, scene) = NewRoom();
        var draft = scene.AddObject(new Lock(Center, "lock.png", null, "", AnswerType.Code, ""));
        var letter = scene.AddObject(new Letter(Center, "letter.png", ""));

        Assert.Equal(
            [
                new RoomValidationError(DomainErrors.LockQuestionMissing, draft.Id),
                new RoomValidationError(DomainErrors.LockAnswerMissing, draft.Id),
                new RoomValidationError(DomainErrors.LetterTextMissing, letter.Id),
            ],
            room.Validate());
    }

    [Theory]
    [InlineData("1")]
    [InlineData("1", " 1 ")]
    [InlineData("1", "")]
    public void MultipleChoiceWithoutTwoDistinctOptions_IsReported(params string[] options)
    {
        var (room, scene) = NewRoom();
        var choice = scene.AddObject(
            new Lock(Center, "lock.png", null, "?", AnswerType.MultipleChoice, "1", options: options));

        Assert.Equal([new RoomValidationError(DomainErrors.LockOptionsInvalid, choice.Id)], room.Validate());
    }

    [Fact]
    public void MultipleChoiceAnswerThatIsNotAnOption_IsReported()
    {
        var (room, scene) = NewRoom();
        var choice = scene.AddObject(
            new Lock(Center, "lock.png", null, "?", AnswerType.MultipleChoice, "5", options: ["1", "2"]));

        Assert.Equal([new RoomValidationError(DomainErrors.LockAnswerNotInOptions, choice.Id)], room.Validate());
    }

    [Fact]
    public void MultipleChoiceAnswer_IsMatchedCaseInsensitively()
    {
        var (room, scene) = NewRoom();
        scene.AddObject(new Lock(Center, "lock.png", null, "?", AnswerType.MultipleChoice, "Paris", options: ["paris", "Rom"]));

        Assert.Empty(room.Validate());
    }

    [Fact]
    public void EmptyTextBox_IsReported_AndATextBoxCannotUnlockAnything()
    {
        var (room, scene) = NewRoom();
        var empty = scene.AddObject(new TextBox(Center, " "));
        var story = scene.AddObject(new TextBox(Center, "Vagten er gået til frokost."));
        var afterStory = scene.AddObject(NewLock());
        afterStory.SetUnlockedBy(story);

        Assert.Equal(
            [
                new RoomValidationError(DomainErrors.TextBoxTextMissing, empty.Id),
                new RoomValidationError(DomainErrors.UnlockedByInvalidTarget, afterStory.Id),
            ],
            room.Validate());
        Assert.Equal(TextBox.GraphicKeyValue, story.GraphicKey);
    }

    [Fact]
    public void TextBox_MayWaitForALock()
    {
        var (room, scene) = NewRoom();
        var lockA = scene.AddObject(NewLock());
        scene.AddObject(new TextBox(Center, "Nu kan I se kortet.")).SetUnlockedBy(lockA);

        Assert.Empty(room.Validate());
    }

    [Fact]
    public void EnsurePlayable_ThrowsRoomInvalid_OnlyWhenThereAreProblems()
    {
        var (room, scene) = NewRoom();
        room.EnsurePlayable();

        scene.AddObject(new Letter(Center, "letter.png", ""));

        Assert.Equal(DomainErrors.RoomInvalid, Assert.Throws<DomainException>(room.EnsurePlayable).Code);
    }
}
