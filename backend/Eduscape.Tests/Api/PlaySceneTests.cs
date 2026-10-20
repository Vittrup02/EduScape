using System.Net;
using System.Net.Http.Json;
using Eduscape.Api.Errors;
using Eduscape.Api.Sessions;
using Eduscape.Domain.Common;
using Eduscape.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Eduscape.Tests.Api;

public class PlaySceneTests(EduscapeApiFactory factory) : IClassFixture<EduscapeApiFactory>
{
    private HttpClient Student => factory.CreateClient();

    [PostgresFact]
    public async Task EnterScene_IsStoredForTheStudent()
    {
        var (room, session) = await StartAsync();
        var ida = await JoinAsync(session);
        await UnlockSecondSceneAsync(room, ida);
        var second = room.Room.Scenes[1];

        var response = await SessionApi.EnterSceneAsync(Student, ida, second.Id);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var db = factory.Database.CreateContext();
        var participant = await db.Participants.SingleAsync(
            p => db.Teams.Any(t => t.Id == p.TeamId && t.SessionId == session.Id));
        Assert.Equal(second.Id, participant.CurrentSceneId);
        Assert.NotNull(participant.SceneEnteredAt);
    }

    [PostgresFact]
    public async Task EnterScene_TheTeamHasNotUnlocked_IsRejected()
    {
        var (room, session) = await StartAsync();
        var ida = await JoinAsync(session);

        var response = await SessionApi.EnterSceneAsync(Student, ida, room.Room.Scenes[1].Id);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(DomainErrors.SceneLocked, (await response.Content.ReadFromJsonAsync<ApiError>())?.Code);
    }

    [PostgresFact]
    public async Task EnterScene_WithUnknownDevice_IsNotRecognized()
    {
        var (room, _) = await StartAsync();

        var response = await SessionApi.EnterSceneAsync(Student, "unknown", room.Room.Scenes[0].Id);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ApiErrors.DeviceNotRecognized, (await response.Content.ReadFromJsonAsync<ApiError>())?.Code);
    }

    private async Task UnlockSecondSceneAsync(PlayTestRoom room, string deviceToken)
    {
        await SessionApi.AnsweredAsync(Student, deviceToken, room.CodeLock.Id, PlayTestRoom.CodeAnswer);
        // The door opens by itself with the scene's last lock.
        await SessionApi.AnsweredAsync(Student, deviceToken, room.TextLock.Id, PlayTestRoom.TextAnswer);
    }

    private async Task<(PlayTestRoom Room, SessionDetailsDto Session)> StartAsync()
    {
        var teacher = await factory.RegisterTeacherAsync();
        var room = await PlayTestRoom.AddAsync(factory, teacher.Id);
        var session = await SessionApi.StartSessionAsync(teacher.Client, room.Room.Id, teamCount: 2);
        return (room, session);
    }

    private async Task<string> JoinAsync(SessionDetailsDto session) =>
        (await SessionApi.JoinedAsync(Student, session.Pin, session.Teams[0].Id, "Ida")).DeviceToken;
}
