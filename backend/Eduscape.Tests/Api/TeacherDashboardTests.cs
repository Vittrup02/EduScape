using System.Net;
using System.Threading.Channels;
using Eduscape.Api.Errors;
using Eduscape.Api.Play;
using Eduscape.Api.Sessions;
using Eduscape.Domain.Common;
using Eduscape.Domain.Sessions;
using Eduscape.Tests.Infrastructure;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;

namespace Eduscape.Tests.Api;

/// <summary>The teacher's live dashboard: its state, moving students and the realtime channel.</summary>
public class TeacherDashboardTests(EduscapeApiFactory factory) : IClassFixture<EduscapeApiFactory>
{
    private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(10);

    private HttpClient Student => factory.CreateClient();

    [PostgresFact]
    public async Task Dashboard_ShowsEachTeamsProgressScenesAndStuckLock()
    {
        var (teacher, room, session) = await StartAsync();
        var ida = await JoinAsync(session, session.Teams[0].Id, "Ida");
        await JoinAsync(session, session.Teams[1].Id, "Bo");
        await SessionApi.AnsweredAsync(Student, ida, room.CodeLock.Id, PlayTestRoom.CodeAnswer);
        await SessionApi.AnsweredAsync(Student, ida, room.TextLock.Id, "wrong");
        await SessionApi.AnsweredAsync(Student, ida, room.TextLock.Id, "still wrong");

        var dashboard = await SessionApi.GetDashboardAsync(teacher.Client, session.Id);

        Assert.Equal(session.Pin, dashboard.Pin);
        Assert.Equal(["First", "Second"], dashboard.Scenes.Select(s => s.Name));
        Assert.Contains(dashboard.Locks, l => l.Id == room.CodeLock.Id && l.Questions.SequenceEqual(["Which code?"]));

        var red = dashboard.Teams.Single(t => t.Id == session.Teams[0].Id);
        Assert.Equal([room.CodeLock.Id], red.SolvedLockIds);
        Assert.Equal(room.TextLock.Id, red.CurrentLock?.LockId);
        Assert.Equal(2, red.CurrentLock?.WrongAttempts);
        var student = Assert.Single(red.Participants);
        Assert.Equal("Ida", student.Nickname);
        Assert.Equal(room.Room.StartScene!.Id, student.SceneId);
        Assert.Equal(1, red.ParticipantCount);

        var blue = dashboard.Teams.Single(t => t.Id == session.Teams[1].Id);
        Assert.Empty(blue.SolvedLockIds);
        Assert.Null(blue.CurrentLock);
    }

    [PostgresFact]
    public async Task Dashboard_NeverContainsAnswersOrHints()
    {
        var (teacher, room, session) = await StartAsync();
        await JoinAsync(session, session.Teams[0].Id, "Ida");

        var response = await teacher.Client.GetAsync($"/api/teacher/sessions/{session.Id}/dashboard");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(PlayTestRoom.CodeAnswer, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(PlayTestRoom.TextAnswer, json);
        Assert.DoesNotContain(PlayTestRoom.Hint, json);
        Assert.DoesNotContain("deviceToken", json, StringComparison.OrdinalIgnoreCase);
    }

    [PostgresFact]
    public async Task Dashboard_OfAnotherTeachersSession_IsNotFound()
    {
        var (_, _, session) = await StartAsync();
        var other = await factory.RegisterTeacherAsync();

        var response = await other.Client.GetAsync($"/api/teacher/sessions/{session.Id}/dashboard");

        await TeacherAuthTests.AssertErrorAsync(response, HttpStatusCode.NotFound, ApiErrors.NotFound);
    }

    [PostgresFact]
    public async Task Move_PutsTheStudentOnTheOtherTeam_AndTellsTheirDevice()
    {
        var (teacher, _, session) = await StartAsync();
        var joined = await SessionApi.JoinedAsync(Student, session.Pin, session.Teams[0].Id, "Ida");
        await using var device = await Connection.ConnectAsync(factory, PlayHub.Path);
        device.On(nameof(IPlayClient.TeamChanged));
        Assert.Null(await device.InvokeAsync(nameof(PlayHub.Subscribe), joined.DeviceToken));

        var response = await SessionApi.MoveAsync(
            teacher.Client, session.Id, joined.State.ParticipantId, session.Teams[1].Id);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(nameof(IPlayClient.TeamChanged), await device.NextEventAsync());
        var state = await SessionApi.ReadAsync<PlayStateDto>(await SessionApi.ReconnectAsync(Student, joined.DeviceToken));
        Assert.Equal(session.Teams[1].Id, state.Team.Id);
    }

    [PostgresFact]
    public async Task Move_InIndividualMode_IsNotAllowed()
    {
        var teacher = await factory.RegisterTeacherAsync();
        var room = await PlayTestRoom.AddAsync(factory, teacher.Id);
        var session = await SessionApi.StartSessionAsync(teacher.Client, room.Room.Id, mode: SessionMode.Individual);
        var ida = await SessionApi.JoinedAsync(Student, session.Pin, null, "Ida");
        await SessionApi.JoinedAsync(Student, session.Pin, null, "Bo");
        var bo = (await SessionApi.GetDashboardAsync(teacher.Client, session.Id)).Teams
            .Single(t => t.Id != ida.State.Team.Id);

        var response = await SessionApi.MoveAsync(teacher.Client, session.Id, ida.State.ParticipantId, bo.Id);

        await TeacherAuthTests.AssertErrorAsync(response, HttpStatusCode.BadRequest, DomainErrors.ParticipantMoveNotAllowed);
    }

    [PostgresFact]
    public async Task Move_InAnotherTeachersSession_IsNotFound()
    {
        var (_, _, session) = await StartAsync();
        var joined = await SessionApi.JoinedAsync(Student, session.Pin, session.Teams[0].Id, "Ida");
        var other = await factory.RegisterTeacherAsync();

        var response = await SessionApi.MoveAsync(
            other.Client, session.Id, joined.State.ParticipantId, session.Teams[1].Id);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [PostgresFact]
    public async Task TeacherHub_WithoutToken_IsRefused()
    {
        var connection = Connection.Build(factory, TeacherHub.Path, accessToken: null);

        await Assert.ThrowsAnyAsync<HttpRequestException>(() => connection.StartAsync());
        await connection.DisposeAsync();
    }

    [PostgresFact]
    public async Task Watch_AnotherTeachersSession_IsNotFound()
    {
        var (_, _, session) = await StartAsync();
        var other = await factory.RegisterTeacherAsync();
        await using var dashboard = await Connection.ConnectAsync(factory, TeacherHub.Path, other.Tokens.AccessToken);

        Assert.Equal(ApiErrors.NotFound, await dashboard.InvokeAsync(nameof(TeacherHub.Watch), session.Id));
    }

    [PostgresFact]
    public async Task Watch_IsToldOfJoinsAnswersScenesAndMoves()
    {
        var (teacher, room, session) = await StartAsync();
        await using var dashboard = await Connection.ConnectAsync(factory, TeacherHub.Path, teacher.Tokens.AccessToken);
        dashboard.On(nameof(ITeacherClient.DashboardChanged));
        Assert.Null(await dashboard.InvokeAsync(nameof(TeacherHub.Watch), session.Id));

        var ida = await SessionApi.JoinedAsync(Student, session.Pin, session.Teams[0].Id, "Ida");
        await dashboard.NextEventAsync();

        await SessionApi.AnsweredAsync(Student, ida.DeviceToken, room.CodeLock.Id, "wrong");
        await dashboard.NextEventAsync();

        await SessionApi.AnsweredAsync(Student, ida.DeviceToken, room.CodeLock.Id, PlayTestRoom.CodeAnswer);
        await SessionApi.AnsweredAsync(Student, ida.DeviceToken, room.TextLock.Id, PlayTestRoom.TextAnswer);
        for (var i = 0; i < 2; i++)
        {
            await dashboard.NextEventAsync();
        }

        (await SessionApi.EnterSceneAsync(Student, ida.DeviceToken, room.Room.Scenes[1].Id)).EnsureSuccessStatusCode();
        await dashboard.NextEventAsync();

        // Reporting the same scene again changes nothing, so the dashboard is not bothered.
        (await SessionApi.EnterSceneAsync(Student, ida.DeviceToken, room.Room.Scenes[1].Id)).EnsureSuccessStatusCode();

        (await SessionApi.MoveAsync(teacher.Client, session.Id, ida.State.ParticipantId, session.Teams[1].Id))
            .EnsureSuccessStatusCode();
        await dashboard.NextEventAsync();
        Assert.False(dashboard.HasEvents);
    }

    [PostgresFact]
    public async Task TeacherHub_AcceptsTheTokenInTheQueryString()
    {
        // Browsers cannot send headers on WebSockets, so the JavaScript client puts the token in the URL.
        var (teacher, _, session) = await StartAsync();
        var path = $"{TeacherHub.Path}?access_token={Uri.EscapeDataString(teacher.Tokens.AccessToken)}";
        await using var dashboard = await Connection.ConnectAsync(factory, path);

        Assert.Null(await dashboard.InvokeAsync(nameof(TeacherHub.Watch), session.Id));
    }

    private async Task<(SignedInTeacher Teacher, PlayTestRoom Room, SessionDetailsDto Session)> StartAsync()
    {
        var teacher = await factory.RegisterTeacherAsync();
        var room = await PlayTestRoom.AddAsync(factory, teacher.Id);
        var session = await SessionApi.StartSessionAsync(teacher.Client, room.Room.Id, teamCount: 2);
        return (teacher, room, session);
    }

    private async Task<string> JoinAsync(SessionDetailsDto session, Guid teamId, string nickname) =>
        (await SessionApi.JoinedAsync(Student, session.Pin, teamId, nickname)).DeviceToken;

    /// <summary>A SignalR connection to the test server that records the (argument-less) events it receives.</summary>
    private sealed class Connection : IAsyncDisposable
    {
        private readonly HubConnection _connection;
        private readonly Channel<string> _events = Channel.CreateUnbounded<string>();

        private Connection(HubConnection connection) => _connection = connection;

        public bool HasEvents => _events.Reader.TryPeek(out _);

        public static HubConnection Build(WebApplicationFactory<Program> app, string path, string? accessToken) =>
            new HubConnectionBuilder()
                .WithUrl(new Uri(app.Server.BaseAddress, path), options =>
                {
                    // The test server has no sockets; long polling runs over its in-memory handler.
                    options.Transports = HttpTransportType.LongPolling;
                    options.HttpMessageHandlerFactory = _ => app.Server.CreateHandler();
                    if (accessToken is not null)
                    {
                        options.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
                    }
                })
                .Build();

        public static async Task<Connection> ConnectAsync(
            WebApplicationFactory<Program> app, string path, string? accessToken = null)
        {
            var connection = Build(app, path, accessToken);
            await connection.StartAsync();
            return new Connection(connection);
        }

        public void On(string eventName) => _connection.On(eventName, () => _events.Writer.TryWrite(eventName));

        public Task<string?> InvokeAsync(string method, object argument) =>
            _connection.InvokeAsync<string?>(method, argument);

        public async Task<string> NextEventAsync()
        {
            using var timeout = new CancellationTokenSource(EventTimeout);
            return await _events.Reader.ReadAsync(timeout.Token);
        }

        public ValueTask DisposeAsync() => _connection.DisposeAsync();
    }
}
