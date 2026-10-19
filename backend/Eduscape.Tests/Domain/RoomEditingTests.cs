using Eduscape.Domain.Common;
using Eduscape.Domain.Rooms;

namespace Eduscape.Tests.Domain;

public class RoomEditingTests
{
    private static readonly Position Center = new(50, 50);

    private static Lock NewLock() => new(Center, "builtin:padlock", null, "?", AnswerType.Code, "1");

    [Fact]
    public void AddScene_KeepsAnIdGivenByTheEditor()
    {
        var room = new EscapeRoom("Rum", Subject.History, GradeLevel.Grade8);
        var id = Guid.NewGuid();

        Assert.Equal(id, room.AddScene("Første", "builtin:classroom", id).Id);
    }

    [Fact]
    public void RemoveScene_ClearsReferencesIntoIt_AndRenumbers()
    {
        var room = new EscapeRoom("Rum", Subject.History, GradeLevel.Grade8);
        var first = room.AddScene("Første", "builtin:classroom");
        var second = room.AddScene("Anden", "builtin:library");
        var third = room.AddScene("Tredje", "builtin:basement");
        var lockInSecond = second.AddObject(NewLock());
        var door = first.AddObject(new Door(Center, "builtin:door", null, second.Id));
        var waiting = third.AddObject(NewLock());
        waiting.SetUnlockedBy(lockInSecond);

        room.RemoveScene(second.Id);

        Assert.Equal([first, third], room.Scenes);
        Assert.Equal(1, third.SortOrder);
        Assert.Null(door.TargetSceneId);
        Assert.Null(waiting.UnlockedById);
        Assert.DoesNotContain(lockInSecond, room.Objects);
    }

    [Fact]
    public void RemoveScene_OfTheStartScene_MakesTheFirstRemainingSceneStart()
    {
        var room = new EscapeRoom("Rum", Subject.History, GradeLevel.Grade8);
        var first = room.AddScene("Første", "builtin:classroom");
        var second = room.AddScene("Anden", "builtin:library");

        room.RemoveScene(first.Id);

        Assert.Same(second, room.StartScene);
        Assert.Equal(0, second.SortOrder);
    }

    [Fact]
    public void RemoveScene_RefusesTheLastSceneAndUnknownScenes()
    {
        var room = new EscapeRoom("Rum", Subject.History, GradeLevel.Grade8);
        var only = room.AddScene("Eneste", "builtin:classroom");

        Assert.Equal(DomainErrors.LastScene, Assert.Throws<DomainException>(() => room.RemoveScene(only.Id)).Code);
        Assert.Equal(
            DomainErrors.SceneNotFound,
            Assert.Throws<DomainException>(() => room.RemoveScene(Guid.NewGuid())).Code);
    }

    [Fact]
    public void MarkEdited_IncreasesTheRevision()
    {
        var room = new EscapeRoom("Rum", Subject.History, GradeLevel.Grade8);

        room.MarkEdited();
        room.MarkEdited();

        Assert.Equal(2, room.Revision);
    }

    [Fact]
    public void CopyFor_CopiesEverything_WithNewIdsAndReferencesIntoTheCopy()
    {
        var template = new EscapeRoom("Skabelon", Subject.Mathematics, GradeLevel.Grade7, isTemplate: true);
        var hall = template.AddScene("Gang", "builtin:classroom");
        var library = template.AddScene("Bibliotek", "builtin:library");
        template.SetStartScene(library.Id);
        var mathLock = hall.AddObject(new Lock(
            new Position(10, 20), "builtin:code-lock", null, "2 + 2?", AnswerType.Text, "4", 0.5m, "Tæl på fingrene"));
        var choice = hall.AddObject(new Lock(
            Center, "builtin:padlock", null, "By?", AnswerType.MultipleChoice, "Paris", options: ["Paris", "Rom"]));
        var door = hall.AddObject(new Door(Center, "builtin:door", null, library.Id));
        door.SetUnlockedBy(mathLock);
        library.AddObject(new Letter(Center, "builtin:letter", "Side 42"));
        library.AddObject(new ExitDoor(Center, "builtin:exit-door", null)).SetUnlockedBy(door);
        var teacherId = Guid.NewGuid();

        var copy = template.CopyFor(teacherId);

        Assert.NotEqual(template.Id, copy.Id);
        Assert.False(copy.IsTemplate);
        Assert.Equal(teacherId, copy.OwnerTeacherId);
        Assert.Equal(("Skabelon", Subject.Mathematics, GradeLevel.Grade7), (copy.Title, copy.Subject, copy.GradeLevel));
        Assert.Equal(["Gang", "Bibliotek"], copy.Scenes.Select(s => s.Name));
        Assert.Equal("Bibliotek", copy.StartScene!.Name);
        Assert.Empty(copy.Validate());

        var originalIds = template.Scenes.Select(s => s.Id).Concat(template.Objects.Select(o => o.Id)).ToHashSet();
        Assert.DoesNotContain(copy.Scenes.Select(s => s.Id).Concat(copy.Objects.Select(o => o.Id)), originalIds.Contains);

        var copiedHall = copy.Scenes[0];
        var copiedLock = Assert.IsType<Lock>(copiedHall.Objects[0]);
        Assert.Equal(
            (mathLock.Position, "2 + 2?", AnswerType.Text, "4", 0.5m, "Tæl på fingrene"),
            (copiedLock.Position, copiedLock.Question, copiedLock.AnswerType, copiedLock.CorrectAnswer,
                copiedLock.Tolerance, copiedLock.Hint));
        Assert.Equal(choice.Options, Assert.IsType<Lock>(copiedHall.Objects[1]).Options);
        var copiedDoor = Assert.IsType<Door>(copiedHall.Objects[2]);
        Assert.Equal(copy.Scenes[1].Id, copiedDoor.TargetSceneId);
        Assert.Equal(copiedLock.Id, copiedDoor.UnlockedById);
        Assert.Equal(copiedDoor.Id, copy.Scenes[1].Objects.OfType<ExitDoor>().Single().UnlockedById);
        Assert.All(copy.Objects, o => Assert.Contains(o.SceneId, copy.Scenes.Select(s => s.Id)));
    }

    [Fact]
    public void CopyFor_DoesNotChangeTheTemplate()
    {
        var template = new EscapeRoom("Skabelon", Subject.Mathematics, GradeLevel.Grade7, isTemplate: true);
        template.AddScene("Gang", "builtin:classroom").AddObject(NewLock());

        template.CopyFor(Guid.NewGuid());

        Assert.True(template.IsTemplate);
        Assert.Null(template.OwnerTeacherId);
        Assert.Single(template.Objects);
    }
}
