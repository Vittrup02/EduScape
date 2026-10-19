using System.Net;
using Eduscape.Api.Editor;
using Eduscape.Api.Errors;
using Eduscape.Api.Play;
using Eduscape.Domain.Common;
using Eduscape.Domain.Rooms;
using Eduscape.Domain.Sessions;
using Eduscape.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Eduscape.Tests.Api.EditorApi;

namespace Eduscape.Tests.Api;

public class PreviewTests(EduscapeApiFactory factory) : IClassFixture<EduscapeApiFactory>
{
    [PostgresFact]
    public async Task Preview_JoinsTheTeacherToAHiddenLockedSession_ThatPlaysLikeAnyOther()
    {
        var teacher = await factory.RegisterTeacherAsync();
        var room = await PlayTestRoom.AddAsync(factory, teacher.Id);

        var preview = await ReadAsync<PreviewResponse>(await PreviewAsync(teacher.Client, room.Room.Id));

        var device = factory.CreateClient();
        var state = await SessionApi.ReadAsync<PlayStateDto>(await SessionApi.ReconnectAsync(device, preview.DeviceToken));
        Assert.True(state.IsPreview);
        Assert.Equal(SessionMode.Individual, state.Mode);
        Assert.Equal(SessionStatus.Locked, state.Status);
        Assert.Equal(room.Room.Id, state.Room.Id);

        // Answers are checked on the server, exactly as for students.
        var answer = await SessionApi.AnsweredAsync(device, preview.DeviceToken, room.CodeLock.Id, PlayTestRoom.CodeAnswer);
        Assert.Equal(AnswerOutcome.Correct, answer.Outcome);

        await using var db = factory.Database.CreateContext();
        var session = await db.Sessions.Include(s => s.Teams).SingleAsync(s => s.Id == state.SessionId);
        Assert.True(session.IsPreview);
        Assert.Null(session.ClassId);
        Assert.Equal(TimeSpan.FromHours(2), session.ExpiresAt - session.StartedAt);
        Assert.Single(session.Teams);

        // Nobody else can join with its PIN, and it is not one of the teacher's sessions.
        await TeacherAuthTests.AssertErrorAsync(
            await SessionApi.JoinAsync(factory.CreateClient(), session.Pin, null, "Elev"),
            HttpStatusCode.BadRequest,
            DomainErrors.SessionLocked);
        Assert.DoesNotContain(await SessionApi.GetActiveSessionsAsync(teacher.Client), s => s.Id == session.Id);
        await TeacherAuthTests.AssertErrorAsync(
            await teacher.Client.GetAsync($"/api/teacher/sessions/{session.Id}"), HttpStatusCode.NotFound, ApiErrors.NotFound);
    }

    [PostgresFact]
    public async Task NormalSession_IsNotAPreview()
    {
        var teacher = await factory.RegisterTeacherAsync();
        var room = await factory.AddRoomAsync(teacher.Id, "Brøker");
        var session = await SessionApi.StartSessionAsync(teacher.Client, room.Id, mode: SessionMode.Individual, teamCount: 0);

        var joined = await SessionApi.JoinedAsync(factory.CreateClient(), session.Pin, null, "Ada");

        Assert.False(joined.State.IsPreview);
    }

    [PostgresFact]
    public async Task UnfinishedRoom_CanBeNeitherPreviewedNorStarted()
    {
        var teacher = await factory.RegisterTeacherAsync();
        var room = await GetRoomAsync(teacher.Client, (await CreateRoomAsync(teacher.Client)).Id);
        await SavedAsync(teacher.Client, room.Id, ToRequest(room) with
        {
            Scenes = [ToRequest(room).Scenes[0] with { Objects = [NewDoor(targetSceneId: null)] }],
        });

        await AssertErrorAsync(await PreviewAsync(teacher.Client, room.Id), HttpStatusCode.BadRequest, DomainErrors.RoomInvalid);
        await AssertErrorAsync(
            await SessionApi.StartAsync(teacher.Client, room.Id), HttpStatusCode.BadRequest, DomainErrors.RoomInvalid);

        await using var db = factory.Database.CreateContext();
        Assert.False(await db.Sessions.AnyAsync(s => s.EscapeRoomId == room.Id));
    }

    [PostgresFact]
    public async Task Preview_OfAnotherTeachersRoom_IsNotFound()
    {
        var alice = await factory.RegisterTeacherAsync();
        var bob = await factory.RegisterTeacherAsync();
        var bobsRoom = await factory.AddRoomAsync(bob.Id, "Bobs rum");

        await AssertErrorAsync(await PreviewAsync(alice.Client, bobsRoom.Id), HttpStatusCode.NotFound, ApiErrors.NotFound);
    }

    [PostgresFact]
    public async Task Preview_OfTheTeachersOwnCopy_ShowsTheEditedRoom()
    {
        var teacher = await factory.RegisterTeacherAsync();
        var room = await GetRoomAsync(teacher.Client, (await CreateRoomAsync(teacher.Client, "Prøv mig")).Id);
        var exit = NewExitDoor();
        await SavedAsync(teacher.Client, room.Id, ToRequest(room) with
        {
            Scenes = [ToRequest(room).Scenes[0] with { Objects = [exit], Narration = "Det er 1943." }],
        });

        var preview = await ReadAsync<PreviewResponse>(await PreviewAsync(teacher.Client, room.Id));
        var state = await SessionApi.ReadAsync<PlayStateDto>(
            await SessionApi.ReconnectAsync(factory.CreateClient(), preview.DeviceToken));

        Assert.Equal("Prøv mig", state.Room.Title);
        var scene = Assert.Single(state.Room.Scenes);
        Assert.Equal(RoomObjectType.ExitDoor, Assert.Single(scene.Objects).Type);
        Assert.Equal("Det er 1943.", scene.Narration);
    }
}
