using System.Net;
using System.Text.Json;
using Eduscape.Api.Errors;
using Eduscape.Api.Play;
using Eduscape.Api.Sessions;
using Eduscape.Domain.Common;
using Eduscape.Domain.Rooms;
using Eduscape.Domain.Sessions;
using Eduscape.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Eduscape.Tests.Api;

public class PlayAnswerTests(EduscapeApiFactory factory) : IClassFixture<EduscapeApiFactory>
{
    private HttpClient Student => factory.CreateClient();

    [PostgresFact]
    public async Task JoinAndReconnect_SendTheRoom_ButNoAnswersToleranceOrUnearnedHints()
    {
        var (room, session) = await StartAsync();

        var joinResponse = await SessionApi.JoinAsync(Student, session.Pin, session.Teams[0].Id, "Ida");
        var joinJson = await joinResponse.Content.ReadAsStringAsync();
        var joined = await SessionApi.ReadAsync<JoinResponse>(joinResponse);
        var reconnectJson = await (await SessionApi.ReconnectAsync(Student, joined.DeviceToken)).Content.ReadAsStringAsync();

        var objectIds = joined.State.Room.Scenes.SelectMany(s => s.Objects).Select(o => o.Id);
        Assert.Equal(room.Room.Objects.Select(o => o.Id).Order(), objectIds.Order());
        var codeLock = joined.State.Room.Scenes.SelectMany(s => s.Objects).Single(o => o.Id == room.CodeLock.Id);
        Assert.Equal("Which code?", codeLock.Question);
        Assert.Equal(AnswerType.Code, codeLock.AnswerType);

        foreach (var json in new[] { joinJson, reconnectJson })
        {
            Assert.DoesNotContain(PlayTestRoom.CodeAnswer, json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(PlayTestRoom.TextAnswer, json);
            Assert.DoesNotContain(PlayTestRoom.Hint, json);
            var names = PropertyNames(JsonDocument.Parse(json).RootElement).ToList();
            Assert.DoesNotContain(names, n => n is "correctAnswer" or "tolerance" or "hint");
        }
    }

    [PostgresFact]
    public async Task CorrectAnswer_SolvesTheLockForTheWholeTeam()
    {
        var (room, session) = await StartAsync();
        var ida = await JoinAsync(session, session.Teams[0].Id);
        var bo = await JoinAsync(session, session.Teams[0].Id);
        var other = await JoinAsync(session, session.Teams[1].Id);

        var answer = await SessionApi.AnsweredAsync(Student, ida, room.CodeLock.Id, $" {PlayTestRoom.CodeAnswer.ToLowerInvariant()} ");

        Assert.Equal(AnswerOutcome.Correct, answer.Outcome);
        Assert.Null(answer.Hint);
        Assert.Equal([room.CodeLock.Id], (await StateAsync(bo)).Team.SolvedLockIds);
        Assert.Empty((await StateAsync(other)).Team.SolvedLockIds);
    }

    [PostgresFact]
    public async Task TextAnswer_IsComparedAsANumber()
    {
        var (room, session) = await StartAsync();
        var ida = await JoinAsync(session, session.Teams[0].Id);
        await SessionApi.AnsweredAsync(Student, ida, room.CodeLock.Id, PlayTestRoom.CodeAnswer);

        Assert.Equal(AnswerOutcome.Wrong, (await SessionApi.AnsweredAsync(Student, ida, room.TextLock.Id, "3.1418")).Outcome);
        Assert.Equal(AnswerOutcome.Correct, (await SessionApi.AnsweredAsync(Student, ida, room.TextLock.Id, "3.1417")).Outcome);
    }

    [PostgresFact]
    public async Task ThirdWrongAnswerFromTheTeam_RevealsTheHintToEveryone()
    {
        var (room, session) = await StartAsync();
        var ida = await JoinAsync(session, session.Teams[0].Id);
        var bo = await JoinAsync(session, session.Teams[0].Id);

        Assert.Null((await SessionApi.AnsweredAsync(Student, ida, room.CodeLock.Id, "1")).Hint);
        Assert.Null((await SessionApi.AnsweredAsync(Student, bo, room.CodeLock.Id, "2")).Hint);
        Assert.Empty((await StateAsync(bo)).Team.RevealedHints);

        var third = await SessionApi.AnsweredAsync(Student, ida, room.CodeLock.Id, "3");

        Assert.Equal(AnswerOutcome.Wrong, third.Outcome);
        Assert.Equal(PlayTestRoom.Hint, third.Hint);
        Assert.Equal([new RevealedHintDto(room.CodeLock.Id, PlayTestRoom.Hint)], (await StateAsync(bo)).Team.RevealedHints);
    }

    [PostgresFact]
    public async Task LockHiddenByUnlockedBy_CannotBeAnswered()
    {
        var (room, session) = await StartAsync();
        var ida = await JoinAsync(session, session.Teams[0].Id);

        var response = await SessionApi.AnswerAsync(Student, ida, room.TextLock.Id, PlayTestRoom.TextAnswer);

        await TeacherAuthTests.AssertErrorAsync(response, HttpStatusCode.BadRequest, DomainErrors.ObjectNotAvailable);
    }

    [PostgresFact]
    public async Task BlankAnswer_IsRejected()
    {
        var (room, session) = await StartAsync();
        var ida = await JoinAsync(session, session.Teams[0].Id);

        var response = await SessionApi.AnswerAsync(Student, ida, room.CodeLock.Id, "  ");

        await TeacherAuthTests.AssertErrorAsync(response, HttpStatusCode.BadRequest, DomainErrors.ValueRequired);
    }

    [PostgresFact]
    public async Task Team_CanOpenTheDoorAndEscape_InOrder()
    {
        var (room, session) = await StartAsync();
        var ida = await JoinAsync(session, session.Teams[0].Id);
        var bo = await JoinAsync(session, session.Teams[0].Id);

        await TeacherAuthTests.AssertErrorAsync(
            await SessionApi.OpenDoorAsync(Student, ida, room.Door.Id), HttpStatusCode.BadRequest, DomainErrors.ObjectNotAvailable);
        await TeacherAuthTests.AssertErrorAsync(
            await SessionApi.OpenExitDoorAsync(Student, ida, room.ExitDoor.Id), HttpStatusCode.BadRequest, DomainErrors.ObjectNotAvailable);

        await SessionApi.AnsweredAsync(Student, ida, room.CodeLock.Id, PlayTestRoom.CodeAnswer);
        await SessionApi.AnsweredAsync(Student, bo, room.TextLock.Id, PlayTestRoom.TextAnswer);
        Assert.Equal(HttpStatusCode.NoContent, (await SessionApi.OpenDoorAsync(Student, bo, room.Door.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SessionApi.OpenDoorAsync(Student, ida, room.Door.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SessionApi.OpenExitDoorAsync(Student, ida, room.ExitDoor.Id)).StatusCode);

        var state = await StateAsync(bo);
        Assert.Equal([room.Door.Id], state.Team.OpenedDoorIds);
        Assert.NotNull(state.Team.EscapedAt);
    }

    [PostgresFact]
    public async Task UnknownDeviceToken_IsNotRecognized()
    {
        var (room, _) = await StartAsync();

        var response = await SessionApi.AnswerAsync(Student, DeviceToken.Create(), room.CodeLock.Id, "1");

        await TeacherAuthTests.AssertErrorAsync(response, HttpStatusCode.NotFound, ApiErrors.DeviceNotRecognized);
    }

    [PostgresFact]
    public async Task ClosedSession_CannotBePlayed()
    {
        var teacher = await factory.RegisterTeacherAsync();
        var room = await PlayTestRoom.AddAsync(factory, teacher.Id);
        var session = await SessionApi.StartSessionAsync(teacher.Client, room.Room.Id);
        var ida = await JoinAsync(session, session.Teams[0].Id);
        (await SessionApi.PostAsync(teacher.Client, session.Id, "close")).EnsureSuccessStatusCode();

        var response = await SessionApi.AnswerAsync(Student, ida, room.CodeLock.Id, PlayTestRoom.CodeAnswer);

        await TeacherAuthTests.AssertErrorAsync(response, HttpStatusCode.BadRequest, DomainErrors.SessionClosed);
    }

    [PostgresFact]
    public async Task LockedSession_CanStillBePlayed()
    {
        var teacher = await factory.RegisterTeacherAsync();
        var room = await PlayTestRoom.AddAsync(factory, teacher.Id);
        var session = await SessionApi.StartSessionAsync(teacher.Client, room.Room.Id);
        var ida = await JoinAsync(session, session.Teams[0].Id);
        (await SessionApi.PostAsync(teacher.Client, session.Id, "lock")).EnsureSuccessStatusCode();

        var answer = await SessionApi.AnsweredAsync(Student, ida, room.CodeLock.Id, PlayTestRoom.CodeAnswer);

        Assert.Equal(AnswerOutcome.Correct, answer.Outcome);
    }

    [PostgresFact]
    public async Task SimultaneousCorrectAnswers_SolveTheLockOnce()
    {
        var (room, session) = await StartAsync();
        var ida = await JoinAsync(session, session.Teams[0].Id);
        var bo = await JoinAsync(session, session.Teams[0].Id);

        var answers = await Task.WhenAll(Enumerable.Range(0, 10).Select(i =>
            SessionApi.AnsweredAsync(Student, i % 2 == 0 ? ida : bo, room.CodeLock.Id, PlayTestRoom.CodeAnswer)));

        Assert.Single(answers, a => a.Outcome == AnswerOutcome.Correct);
        Assert.All(answers, a => Assert.NotEqual(AnswerOutcome.Wrong, a.Outcome));

        await using var db = factory.Database.CreateContext();
        var progress = await db.Teams
            .Where(t => t.Id == session.Teams[0].Id)
            .SelectMany(t => t.LockProgress)
            .ToListAsync();
        var solved = Assert.Single(progress);
        Assert.NotNull(solved.SolvedAt);
        Assert.Equal(0, solved.WrongAttempts);
    }

    [PostgresFact]
    public async Task SimultaneousWrongAnswers_AreAllCounted()
    {
        var (room, session) = await StartAsync();
        var ida = await JoinAsync(session, session.Teams[0].Id);
        var bo = await JoinAsync(session, session.Teams[0].Id);

        var answers = await Task.WhenAll(Enumerable.Range(0, 6).Select(i =>
            SessionApi.AnsweredAsync(Student, i % 2 == 0 ? ida : bo, room.CodeLock.Id, $"wrong {i}")));

        Assert.All(answers, a => Assert.Equal(AnswerOutcome.Wrong, a.Outcome));
        await using var db = factory.Database.CreateContext();
        var progress = await db.Teams
            .Where(t => t.Id == session.Teams[0].Id)
            .SelectMany(t => t.LockProgress)
            .SingleAsync();
        Assert.Equal(6, progress.WrongAttempts);
    }

    private async Task<(PlayTestRoom Room, SessionDetailsDto Session)> StartAsync()
    {
        var teacher = await factory.RegisterTeacherAsync();
        var room = await PlayTestRoom.AddAsync(factory, teacher.Id);
        var session = await SessionApi.StartSessionAsync(teacher.Client, room.Room.Id, teamCount: 2);
        return (room, session);
    }

    private async Task<string> JoinAsync(SessionDetailsDto session, Guid teamId) =>
        (await SessionApi.JoinedAsync(Student, session.Pin, teamId, nickname: null)).DeviceToken;

    private async Task<PlayStateDto> StateAsync(string deviceToken) =>
        await SessionApi.ReadAsync<PlayStateDto>(await SessionApi.ReconnectAsync(Student, deviceToken));

    private static IEnumerable<string> PropertyNames(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().SelectMany(p => PropertyNames(p.Value).Prepend(p.Name)),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(PropertyNames),
        _ => [],
    };
}
