using System.Net;
using Eduscape.Api.Editor;
using Eduscape.Api.Errors;
using Eduscape.Domain.Common;
using Eduscape.Domain.Rooms;
using Eduscape.Infrastructure.Persistence.Seeding;
using Eduscape.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Eduscape.Tests.Api.EditorApi;

namespace Eduscape.Tests.Api;

public class EditorEndpointTests(EduscapeApiFactory factory) : IClassFixture<EduscapeApiFactory>
{
    [PostgresFact]
    public async Task CreateRoom_StartsWithOneEmptyStartScene()
    {
        var teacher = await factory.RegisterTeacherAsync();

        var created = await CreateRoomAsync(teacher.Client, " Brøker ");
        var room = await GetRoomAsync(teacher.Client, created.Id);

        Assert.Equal(("Brøker", Subject.Mathematics, GradeLevel.Grade6, 0), (room.Title, room.Subject, room.GradeLevel, room.Revision));
        var scene = Assert.Single(room.Scenes);
        Assert.Equal(("Scene 1", EditorEndpoints.DefaultBackground), (scene.Name, scene.BackgroundImageKey));
        Assert.Equal(scene.Id, room.StartSceneId);
        Assert.Null(scene.BackgroundImageUrl);
        // The editor tells the teacher straight away what an escape room needs.
        Assert.Equal([new RoomValidationErrorDto(DomainErrors.ExitDoorMissing, null)], room.ValidationErrors);
        Assert.False(room.UploadsEnabled);
    }

    [PostgresFact]
    public async Task Save_RoundTripsTheWholeRoom_IncludingAnswersForTheTeacher()
    {
        var teacher = await factory.RegisterTeacherAsync();
        var room = await GetRoomAsync(teacher.Client, (await CreateRoomAsync(teacher.Client)).Id);
        var library = NewScene("Bibliotek", NewExitDoor());
        var mathLock = NewLock([NewStep() with
        {
            AnswerType = AnswerType.Text,
            CorrectAnswer = "3,14",
            Tolerance = 0.01m,
            Hint = "Tænk på cirkler",
        }]);
        // A lock with three steps: a typed answer with alternatives, multiple choice and an order.
        var reaction = NewLock(
        [
            NewStep("Hvad hedder H2O?", "vand", AnswerType.Text) with { AcceptedAnswers = ["dihydrogenoxid"], AcceptTypos = true },
            NewStep("By?", "Paris", AnswerType.MultipleChoice) with { Options = ["Paris", "Rom"] },
            NewStep("Sæt trinene i rækkefølge", "", AnswerType.Order) with
            {
                OrderItems = [new(Guid.NewGuid(), "Afvej"), new(Guid.NewGuid(), "Opløs"), new(Guid.NewGuid(), "Filtrér")],
            },
        ]);
        var door = NewDoor(library.Id, mathLock.Id);
        var letter = NewLetter();
        var start = room.Scenes[0] with { Name = "Gangen" };
        var request = ToRequest(room) with
        {
            Title = "Cirkler",
            Subject = Subject.PhysicsChemistry,
            GradeLevel = GradeLevel.Grade9,
            WrongAnswerCooldownSeconds = 30,
            Scenes =
            [
                // The library comes first in the strip, but students start in the hall.
                library,
                new SaveSceneRequest(start.Id, start.Name, "builtin:basement", [mathLock, reaction, door, letter], " Det er 1943. "),
            ],
        };

        var saved = await SavedAsync(teacher.Client, room.Id, request);
        var loaded = await GetRoomAsync(teacher.Client, room.Id);

        Assert.Equal(1, saved.Revision);
        Assert.Empty(saved.ValidationErrors);
        Assert.Equal(("Cirkler", Subject.PhysicsChemistry, GradeLevel.Grade9, 1), (loaded.Title, loaded.Subject, loaded.GradeLevel, loaded.Revision));
        Assert.Equal(30, loaded.WrongAnswerCooldownSeconds);
        Assert.Equal(["Bibliotek", "Gangen"], loaded.Scenes.Select(s => s.Name));
        Assert.Equal(start.Id, loaded.StartSceneId);
        Assert.Equal("builtin:basement", loaded.Scenes[1].BackgroundImageKey);
        Assert.Equal("Det er 1943.", loaded.Scenes[1].Narration);
        Assert.Null(loaded.Scenes[0].Narration);
        var objects = loaded.Scenes[1].Objects.ToDictionary(o => o.Id);
        Assert.Equal(4, objects.Count);
        var loadedLock = objects[mathLock.Id];
        var step = Assert.Single(loadedLock.Steps!);
        Assert.Equal(
            (30d, 40d, mathLock.Steps![0].Id, "2 + 2?", AnswerType.Text, "3,14", 0.01m, "Tænk på cirkler"),
            (loadedLock.X, loadedLock.Y, step.Id, step.Question, step.AnswerType, step.CorrectAnswer, step.Tolerance, step.Hint));
        var steps = objects[reaction.Id].Steps!;
        Assert.Equal(reaction.Steps!.Select(s => s.Id), steps.Select(s => s.Id));
        Assert.Equal(["dihydrogenoxid"], steps[0].AcceptedAnswers!);
        Assert.True(steps[0].AcceptTypos);
        Assert.Equal(["Paris", "Rom"], steps[1].Options!);
        Assert.Equal(["Afvej", "Opløs", "Filtrér"], steps[2].OrderItems!.Select(i => i.Text));
        Assert.Equal(reaction.Steps[2].OrderItems!.Select(i => i.Id), steps[2].OrderItems!.Select(i => i.Id));
        Assert.Equal((library.Id, DoorOpenCondition.SelectedLocks), (objects[door.Id].TargetSceneId, objects[door.Id].OpenCondition));
        Assert.Equal([mathLock.Id], objects[door.Id].RequiredLockIds!);
        Assert.Null(objects[door.Id].UnlockedById);
        Assert.Equal("Læs side 42", objects[letter.Id].Text);
        var exit = Assert.Single(loaded.Scenes[0].Objects);
        Assert.Equal((RoomObjectType.ExitDoor, DoorOpenCondition.AllInScene), (exit.Type, exit.OpenCondition));
    }

    [PostgresFact]
    public async Task Save_ReportsProblemsInTheStepTheyAreIn()
    {
        var teacher = await factory.RegisterTeacherAsync();
        var room = await GetRoomAsync(teacher.Client, (await CreateRoomAsync(teacher.Client)).Id);
        var twoSteps = NewLock([NewStep(), NewStep(question: "", answer: "") with { AnswerType = AnswerType.Order }]);
        var noPuzzle = NewLock([]);
        var request = ToRequest(room) with
        {
            Scenes = [ToRequest(room).Scenes[0] with { Objects = [twoSteps, noPuzzle, NewExitDoor()] }],
        };

        var saved = await SavedAsync(teacher.Client, room.Id, request);

        Assert.Equal(
            [
                new RoomValidationErrorDto(DomainErrors.LockQuestionMissing, twoSteps.Id, 1),
                new RoomValidationErrorDto(DomainErrors.OrderItemsInvalid, twoSteps.Id, 1),
                new RoomValidationErrorDto(DomainErrors.LockPuzzleMissing, noPuzzle.Id),
            ],
            saved.ValidationErrors);
    }

    [PostgresFact]
    public async Task TextBox_IsSavedWithItsText_AndStudentsSeeItOnTheScene()
    {
        var teacher = await factory.RegisterTeacherAsync();
        var room = await GetRoomAsync(teacher.Client, (await CreateRoomAsync(teacher.Client)).Id);
        var textBox = new EditorObjectDto(
            Guid.CreateVersion7(), RoomObjectType.TextBox, 25, 15, "builtin:text-box", null, Text: "Vagten er gået til frokost.");
        var exit = NewExitDoor();
        var request = ToRequest(room) with { Scenes = [ToRequest(room).Scenes[0] with { Objects = [textBox, exit] }] };

        var saved = await SavedAsync(teacher.Client, room.Id, request);
        var loaded = (await GetRoomAsync(teacher.Client, room.Id)).Scenes[0].Objects.Single(o => o.Id == textBox.Id);
        Assert.Empty(saved.ValidationErrors);
        Assert.Equal(
            (RoomObjectType.TextBox, 25d, 15d, "Vagten er gået til frokost."),
            (loaded.Type, loaded.X, loaded.Y, loaded.Text));

        // Moving it and changing the text keeps it a text box.
        await SavedAsync(teacher.Client, room.Id, request with
        {
            Revision = saved.Revision,
            Scenes = [request.Scenes[0] with { Objects = [textBox with { X = 60, Text = "Vagten er tilbage!" }, exit] }],
        });

        var preview = await ReadAsync<PreviewResponse>(await PreviewAsync(teacher.Client, room.Id));
        var state = await SessionApi.ReadAsync<Eduscape.Api.Play.PlayStateDto>(
            await SessionApi.ReconnectAsync(factory.CreateClient(), preview.DeviceToken));
        var played = Assert.Single(state.Room.Scenes).Objects.Single(o => o.Id == textBox.Id);
        Assert.Equal((RoomObjectType.TextBox, 60d, "Vagten er tilbage!"), (played.Type, played.X, played.Text));
    }

    [PostgresFact]
    public async Task Save_MovesUpdatesAndRemovesObjectsAndScenes()
    {
        var teacher = await factory.RegisterTeacherAsync();
        var room = await GetRoomAsync(teacher.Client, (await CreateRoomAsync(teacher.Client)).Id);
        var second = NewScene("Anden");
        var lockA = NewLock();
        var letter = NewLetter();
        var door = NewDoor(second.Id);
        await SavedAsync(teacher.Client, room.Id, ToRequest(room) with
        {
            Scenes = [new SaveSceneRequest(room.Scenes[0].Id, "Første", "builtin:classroom", [lockA, letter, door]), second],
        });

        // Move the lock, change its answer, drop the letter, and remove the second scene (the door loses its target).
        var loaded = await GetRoomAsync(teacher.Client, room.Id);
        var request = ToRequest(loaded) with
        {
            Scenes =
            [
                new SaveSceneRequest(room.Scenes[0].Id, "Første", "builtin:classroom",
                [
                    lockA with { X = 70, Y = 10, Steps = [lockA.Steps![0] with { CorrectAnswer = "5" }] },
                    door with { TargetSceneId = null },
                ]),
            ],
        };
        var saved = await SavedAsync(teacher.Client, room.Id, request);
        var reloaded = await GetRoomAsync(teacher.Client, room.Id);

        Assert.Equal(2, saved.Revision);
        Assert.Equal(
            [
                new RoomValidationErrorDto(DomainErrors.ExitDoorMissing, null),
                new RoomValidationErrorDto(DomainErrors.DoorTargetMissing, door.Id),
            ],
            saved.ValidationErrors);
        Assert.Equal(saved.ValidationErrors, reloaded.ValidationErrors);
        var scene = Assert.Single(reloaded.Scenes);
        var movedLock = scene.Objects.Single(o => o.Id == lockA.Id);
        Assert.Equal((70d, 10d, "5"), (movedLock.X, movedLock.Y, movedLock.Steps![0].CorrectAnswer));
        Assert.DoesNotContain(scene.Objects, o => o.Id == letter.Id);
        await using var db = factory.Database.CreateContext();
        Assert.False(await db.Scenes.AnyAsync(s => s.Id == second.Id));
    }

    [PostgresFact]
    public async Task Save_CanChangeTheStartScene_ToAnExistingOrANewScene()
    {
        var teacher = await factory.RegisterTeacherAsync();
        var room = await GetRoomAsync(teacher.Client, (await CreateRoomAsync(teacher.Client)).Id);
        var first = room.Scenes[0];
        var second = NewScene("Anden");

        await SavedAsync(teacher.Client, room.Id, ToRequest(room) with
        {
            StartSceneId = second.Id,
            Scenes = [new SaveSceneRequest(first.Id, first.Name, first.BackgroundImageKey, []), second],
        });
        var loaded = await GetRoomAsync(teacher.Client, room.Id);
        Assert.Equal(second.Id, loaded.StartSceneId);

        await SavedAsync(teacher.Client, room.Id, ToRequest(loaded) with { StartSceneId = first.Id });
        Assert.Equal(first.Id, (await GetRoomAsync(teacher.Client, room.Id)).StartSceneId);

        // Removing the start scene: the remaining scene takes over.
        var withoutFirst = ToRequest(await GetRoomAsync(teacher.Client, room.Id));
        await SavedAsync(teacher.Client, room.Id, withoutFirst with { StartSceneId = second.Id, Scenes = [withoutFirst.Scenes[1]] });
        Assert.Equal(second.Id, (await GetRoomAsync(teacher.Client, room.Id)).StartSceneId);
    }

    [PostgresFact]
    public async Task Save_KeepsUnfinishedObjects_AndReportsThem()
    {
        var teacher = await factory.RegisterTeacherAsync();
        var room = await GetRoomAsync(teacher.Client, (await CreateRoomAsync(teacher.Client)).Id);
        var draft = NewLock(question: "", answer: "");
        var letter = NewLetter(text: "");
        var request = ToRequest(room) with
        {
            Scenes = [new SaveSceneRequest(room.Scenes[0].Id, room.Scenes[0].Name, "builtin:classroom", [draft, letter, NewExitDoor()])],
        };

        var saved = await SavedAsync(teacher.Client, room.Id, request);

        Assert.Equal(
            [
                new RoomValidationErrorDto(DomainErrors.LockQuestionMissing, draft.Id, 0),
                new RoomValidationErrorDto(DomainErrors.LockAnswerMissing, draft.Id, 0),
                new RoomValidationErrorDto(DomainErrors.LetterTextMissing, letter.Id),
            ],
            saved.ValidationErrors.OrderBy(e => e.ObjectId == letter.Id).ThenBy(e => e.Code == DomainErrors.LockAnswerMissing));
    }

    [PostgresFact]
    public async Task Save_IgnoresSettingsThatDoNotApplyToTheAnswerType()
    {
        var teacher = await factory.RegisterTeacherAsync();
        var room = await GetRoomAsync(teacher.Client, (await CreateRoomAsync(teacher.Client)).Id);
        var codeLock = NewLock([NewStep() with
        {
            Options = ["kept", "while editing"],
            Tolerance = 0.5m,
            AcceptedAnswers = ["fire"],
            AcceptTypos = true,
            OrderItems = [new(Guid.NewGuid(), "A")],
        }]);

        await SavedAsync(teacher.Client, room.Id, ToRequest(room) with
        {
            Scenes = [new SaveSceneRequest(room.Scenes[0].Id, "Scene", "builtin:classroom", [codeLock])],
        });
        var loaded = Assert.Single(Assert.Single((await GetRoomAsync(teacher.Client, room.Id)).Scenes[0].Objects).Steps!);

        Assert.Empty(loaded.Options!);
        Assert.Null(loaded.Tolerance);
        Assert.Empty(loaded.AcceptedAnswers!);
        Assert.False(loaded.AcceptTypos);
        Assert.Empty(loaded.OrderItems!);
    }

    [PostgresFact]
    public async Task Save_BasedOnAnOlderRevision_IsRefused()
    {
        var teacher = await factory.RegisterTeacherAsync();
        var room = await GetRoomAsync(teacher.Client, (await CreateRoomAsync(teacher.Client)).Id);
        await SavedAsync(teacher.Client, room.Id, ToRequest(room) with { Title = "Første fane" });

        var response = await SaveAsync(teacher.Client, room.Id, ToRequest(room) with { Title = "Anden fane" });

        await AssertErrorAsync(response, HttpStatusCode.Conflict, ApiErrors.RoomChanged);
        Assert.Equal("Første fane", (await GetRoomAsync(teacher.Client, room.Id)).Title);
    }

    [PostgresFact]
    public async Task Save_WithTheIdOfSomethingElse_IsRefused()
    {
        var teacher = await factory.RegisterTeacherAsync();
        var room = await GetRoomAsync(teacher.Client, (await CreateRoomAsync(teacher.Client)).Id);
        await using var db = factory.Database.CreateContext();
        var templateSceneId = await db.Scenes
            .Where(s => s.EscapeRoomId == DemoRoomSeeder.DemoRoomId)
            .Select(s => s.Id)
            .FirstAsync();
        var request = ToRequest(room);

        var response = await SaveAsync(teacher.Client, room.Id, request with
        {
            Scenes = [.. request.Scenes, new SaveSceneRequest(templateSceneId, "Stjålet", "builtin:classroom", [])],
        });

        await AssertErrorAsync(response, HttpStatusCode.Conflict, ApiErrors.IdTaken);
    }

    public static TheoryData<string, string> InvalidDocuments() => new()
    {
        { "reference outside the document", ApiErrors.DocumentInvalid },
        { "door to a scene outside the document", ApiErrors.DocumentInvalid },
        { "duplicate id", ApiErrors.DocumentInvalid },
        { "no scenes", ApiErrors.DocumentInvalid },
        { "unknown start scene", ApiErrors.DocumentInvalid },
        { "lock without steps", ApiErrors.DocumentInvalid },
        { "step with unknown answer type", ApiErrors.DocumentInvalid },
        { "door waiting with unlockedBy", ApiErrors.DocumentInvalid },
        { "door opening with a lock outside the document", ApiErrors.DocumentInvalid },
        { "order item with a time-ordered id", ApiErrors.DocumentInvalid },
        { "order item with a free image URL", DomainErrors.ImageKeyInvalid },
        { "image key", DomainErrors.ImageKeyInvalid },
        { "position", DomainErrors.PositionOutOfRange },
        { "too many scenes", ApiErrors.RoomTooLarge },
        { "negative wait", DomainErrors.CooldownInvalid },
    };

    [PostgresTheory]
    [MemberData(nameof(InvalidDocuments))]
    public async Task Save_InvalidDocument_ChangesNothing(string problem, string code)
    {
        var teacher = await factory.RegisterTeacherAsync();
        var room = await GetRoomAsync(teacher.Client, (await CreateRoomAsync(teacher.Client)).Id);
        var request = ToRequest(room);
        var scene = request.Scenes[0];
        var lockA = NewLock();
        SaveSceneRequest WithObjects(params EditorObjectDto[] objects) => scene with { Objects = objects };
        EditorObjectDto OrderLock(EditorOrderItemDto item) => NewLock([NewStep(answerType: AnswerType.Order) with
        {
            OrderItems = [item, new EditorOrderItemDto(Guid.NewGuid(), "B")],
        }]);

        var invalid = problem switch
        {
            "reference outside the document" => request with { Scenes = [WithObjects(lockA with { UnlockedById = Guid.NewGuid() })] },
            "door to a scene outside the document" => request with { Scenes = [WithObjects(NewDoor(Guid.NewGuid()))] },
            "duplicate id" => request with { Scenes = [WithObjects(lockA, NewLetter() with { Id = lockA.Id })] },
            "no scenes" => request with { Scenes = [] },
            "unknown start scene" => request with { StartSceneId = Guid.NewGuid() },
            "lock without steps" => request with { Scenes = [WithObjects(lockA with { Steps = null })] },
            "step with unknown answer type" => request with
            {
                Scenes = [WithObjects(lockA with { Steps = [NewStep() with { AnswerType = (AnswerType)42 }] })],
            },
            "door waiting with unlockedBy" => request with
            {
                Scenes = [WithObjects(lockA, NewDoor(scene.Id) with { UnlockedById = lockA.Id })],
            },
            "door opening with a lock outside the document" => request with
            {
                Scenes = [WithObjects(NewDoor(scene.Id, Guid.NewGuid()))],
            },
            "order item with a time-ordered id" => request with
            {
                Scenes = [WithObjects(OrderLock(new EditorOrderItemDto(Guid.CreateVersion7(), "A")))],
            },
            "order item with a free image URL" => request with
            {
                Scenes = [WithObjects(OrderLock(new EditorOrderItemDto(Guid.NewGuid(), "A", "https://example.com/a.png")))],
            },
            "image key" => request with { Scenes = [scene with { BackgroundImageKey = "https://example.com/cat.jpg" }] },
            "position" => request with { Scenes = [WithObjects(lockA with { X = 101 })] },
            "too many scenes" => request with
            {
                Scenes = [scene, .. Enumerable.Range(0, RoomDocumentApplier.MaxScenes).Select(i => NewScene($"Scene {i}"))],
            },
            "negative wait" => request with { WrongAnswerCooldownSeconds = -1 },
            _ => throw new ArgumentOutOfRangeException(nameof(problem)),
        };

        var response = await SaveAsync(teacher.Client, room.Id, invalid);

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, code);
        Assert.Equal(0, (await GetRoomAsync(teacher.Client, room.Id)).Revision);
    }

    [PostgresFact]
    public async Task Save_ChangingTheTypeOfAnObject_IsRefused()
    {
        var teacher = await factory.RegisterTeacherAsync();
        var room = await GetRoomAsync(teacher.Client, (await CreateRoomAsync(teacher.Client)).Id);
        var letter = NewLetter();
        var request = ToRequest(room) with { Scenes = [ToRequest(room).Scenes[0] with { Objects = [letter] }] };
        var saved = await SavedAsync(teacher.Client, room.Id, request);

        var response = await SaveAsync(teacher.Client, room.Id, request with
        {
            Revision = saved.Revision,
            Scenes = [request.Scenes[0] with { Objects = [NewExitDoor() with { Id = letter.Id }] }],
        });

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, ApiErrors.DocumentInvalid);
    }

    [PostgresFact]
    public async Task Editor_OfAnotherTeachersRoomOrATemplate_IsNotFound()
    {
        var alice = await factory.RegisterTeacherAsync();
        var bob = await factory.RegisterTeacherAsync();
        var bobsRoom = await GetRoomAsync(bob.Client, (await CreateRoomAsync(bob.Client)).Id);

        foreach (var id in new[] { bobsRoom.Id, DemoRoomSeeder.DemoRoomId })
        {
            await AssertErrorAsync(await GetAsync(alice.Client, id), HttpStatusCode.NotFound, ApiErrors.NotFound);
            await AssertErrorAsync(
                await SaveAsync(alice.Client, id, ToRequest(bobsRoom) with { Title = "Overtaget" }),
                HttpStatusCode.NotFound,
                ApiErrors.NotFound);
        }

        Assert.Equal(bobsRoom.Title, (await GetRoomAsync(bob.Client, bobsRoom.Id)).Title);
    }

    [PostgresFact]
    public async Task CopyTemplate_GivesTheTeacherAnEditableCopy()
    {
        var teacher = await factory.RegisterTeacherAsync();

        var response = await CopyTemplateAsync(teacher.Client, DemoRoomSeeder.DemoRoomId);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var copy = await GetRoomAsync(teacher.Client, (await ReadAsync<Eduscape.Api.Teachers.RoomSummaryDto>(response)).Id);

        await using var db = factory.Database.CreateContext();
        var template = await db.EscapeRooms
            .Include(r => r.Scenes).ThenInclude(s => s.Objects)
            .AsSplitQuery()
            .SingleAsync(r => r.Id == DemoRoomSeeder.DemoRoomId);
        Assert.NotEqual(template.Id, copy.Id);
        Assert.Equal(template.Title, copy.Title);
        Assert.Equal(template.Scenes.Count, copy.Scenes.Length);
        Assert.Equal(template.Objects.Count(), copy.Scenes.Sum(s => s.Objects.Length));
        Assert.Empty(copy.ValidationErrors);
        Assert.DoesNotContain(copy.Scenes.SelectMany(s => s.Objects), o => template.Objects.Any(t => t.Id == o.Id));

        // The copy is the teacher's own and can be saved; the template stays as it was.
        await SavedAsync(teacher.Client, copy.Id, ToRequest(copy) with { Title = "Min udgave" });
        Assert.Equal(template.Title, (await db.EscapeRooms.AsNoTracking().SingleAsync(r => r.Id == template.Id)).Title);
    }

    [PostgresFact]
    public async Task CopyTemplate_OfAnOrdinaryRoom_IsNotFound()
    {
        var alice = await factory.RegisterTeacherAsync();
        var bob = await factory.RegisterTeacherAsync();
        var bobsRoom = await CreateRoomAsync(bob.Client);

        await AssertErrorAsync(await CopyTemplateAsync(alice.Client, bobsRoom.Id), HttpStatusCode.NotFound, ApiErrors.NotFound);
    }
}
