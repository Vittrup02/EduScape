using System.Collections.Concurrent;
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
using Microsoft.Extensions.DependencyInjection;

namespace Eduscape.Tests.Api;

public class PlayHubTests(EduscapeApiFactory factory) : IClassFixture<EduscapeApiFactory>
{
    private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(10);

    private HttpClient Student => factory.CreateClient();

    [PostgresFact]
    public async Task Subscribe_WithUnknownToken_ReturnsDeviceNotRecognized()
    {
        await using var device = await TeamDevice.ConnectAsync(factory);

        Assert.Equal(ApiErrors.DeviceNotRecognized, await device.SubscribeAsync(DeviceToken.Create()));
        Assert.Equal(ApiErrors.DeviceNotRecognized, await device.SubscribeAsync(""));
    }

    [PostgresFact]
    public async Task Subscribe_ToClosedSession_ReturnsSessionClosed()
    {
        var teacher = await factory.RegisterTeacherAsync();
        var room = await PlayTestRoom.AddAsync(factory, teacher.Id);
        var session = await SessionApi.StartSessionAsync(teacher.Client, room.Room.Id);
        var ida = await JoinAsync(session, session.Teams[0].Id);
        (await SessionApi.PostAsync(teacher.Client, session.Id, "close")).EnsureSuccessStatusCode();
        await using var device = await TeamDevice.ConnectAsync(factory);

        Assert.Equal(DomainErrors.SessionClosed, await device.SubscribeAsync(ida));
    }

    [PostgresFact]
    public async Task ClosingTheSession_TellsEveryTeamsDevices()
    {
        var teacher = await factory.RegisterTeacherAsync();
        var room = await PlayTestRoom.AddAsync(factory, teacher.Id);
        var session = await SessionApi.StartSessionAsync(teacher.Client, room.Room.Id, teamCount: 2);
        var ida = await JoinAsync(session, session.Teams[0].Id);
        var bo = await JoinAsync(session, session.Teams[1].Id);
        await using var idaDevice = await TeamDevice.ConnectAsync(factory);
        await using var boDevice = await TeamDevice.ConnectAsync(factory);
        Assert.Null(await idaDevice.SubscribeAsync(ida));
        Assert.Null(await boDevice.SubscribeAsync(bo));

        (await SessionApi.PostAsync(teacher.Client, session.Id, "close")).EnsureSuccessStatusCode();

        Assert.Equal("SessionClosed", await idaDevice.NextEventAsync());
        Assert.Equal("SessionClosed", await boDevice.NextEventAsync());
    }

    [PostgresFact]
    public async Task TeamProgress_IsPushedToTeammates_ButNotToOtherTeams()
    {
        var (room, session) = await StartAsync();
        var ida = await JoinAsync(session, session.Teams[0].Id);
        var bo = await JoinAsync(session, session.Teams[0].Id);
        var other = await JoinAsync(session, session.Teams[1].Id);
        await using var boDevice = await TeamDevice.ConnectAsync(factory);
        await using var otherDevice = await TeamDevice.ConnectAsync(factory);
        Assert.Null(await boDevice.SubscribeAsync(bo));
        Assert.Null(await otherDevice.SubscribeAsync(other));

        for (var i = 0; i < 3; i++)
        {
            await SessionApi.AnsweredAsync(Student, ida, room.CodeLock.Id, $"wrong {i}");
        }

        Assert.Equal($"HintRevealed {room.CodeLock.Id} {PlayTestRoom.Hint}", await boDevice.NextEventAsync());

        await SessionApi.AnsweredAsync(Student, ida, room.CodeLock.Id, PlayTestRoom.CodeAnswer);
        Assert.Equal($"LockSolved {room.CodeLock.Id}", await boDevice.NextEventAsync());

        await SessionApi.AnsweredAsync(Student, ida, room.TextLock.Id, PlayTestRoom.TextAnswer);
        Assert.Equal($"LockSolved {room.TextLock.Id}", await boDevice.NextEventAsync());

        await SessionApi.OpenDoorAsync(Student, ida, room.Door.Id);
        Assert.Equal($"DoorOpened {room.Door.Id}", await boDevice.NextEventAsync());

        await SessionApi.OpenExitDoorAsync(Student, ida, room.ExitDoor.Id);
        Assert.Equal("TeamEscaped", await boDevice.NextEventAsync());

        // Every event to Bo's team has arrived; anything sent to the other team would have arrived by now as well.
        Assert.False(otherDevice.HasEvents);
    }

    [PostgresFact]
    public async Task SimultaneousCorrectAnswers_NotifyTheTeamOnce()
    {
        var notifier = new RecordingTeamNotifier();
        var app = factory.WithServices(services => services.AddSingleton<ITeamNotifier>(notifier));
        var teacher = await factory.RegisterTeacherAsync(app);
        var room = await PlayTestRoom.AddAsync(factory, teacher.Id);
        var session = await SessionApi.StartSessionAsync(teacher.Client, room.Room.Id);
        var student = app.CreateClient();
        var ida = (await SessionApi.JoinedAsync(student, session.Pin, session.Teams[0].Id, null)).DeviceToken;
        var bo = (await SessionApi.JoinedAsync(student, session.Pin, session.Teams[0].Id, null)).DeviceToken;

        await Task.WhenAll(Enumerable.Range(0, 10).Select(i =>
            SessionApi.AnsweredAsync(app.CreateClient(), i % 2 == 0 ? ida : bo, room.CodeLock.Id, PlayTestRoom.CodeAnswer)));

        Assert.Equal([$"LockSolved {session.Teams[0].Id} {room.CodeLock.Id}"], notifier.Events);
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

    /// <summary>A student's SignalR connection to the test server, recording the events it receives.</summary>
    private sealed class TeamDevice : IAsyncDisposable
    {
        private readonly HubConnection _connection;
        private readonly Channel<string> _events = Channel.CreateUnbounded<string>();

        private TeamDevice(HubConnection connection)
        {
            _connection = connection;
            _connection.On<Guid>(nameof(IPlayClient.LockSolved), id => Record($"LockSolved {id}"));
            _connection.On<Guid, string>(nameof(IPlayClient.HintRevealed), (id, hint) => Record($"HintRevealed {id} {hint}"));
            _connection.On<Guid>(nameof(IPlayClient.DoorOpened), id => Record($"DoorOpened {id}"));
            _connection.On(nameof(IPlayClient.TeamEscaped), () => Record("TeamEscaped"));
            _connection.On(nameof(IPlayClient.SessionClosed), () => Record("SessionClosed"));
        }

        public bool HasEvents => _events.Reader.TryPeek(out _);

        public static async Task<TeamDevice> ConnectAsync(WebApplicationFactory<Program> app)
        {
            var server = app.Server;
            var connection = new HubConnectionBuilder()
                .WithUrl(new Uri(server.BaseAddress, PlayHub.Path), options =>
                {
                    // The test server has no sockets; long polling runs over its in-memory handler.
                    options.Transports = HttpTransportType.LongPolling;
                    options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                })
                .Build();
            await connection.StartAsync();
            return new TeamDevice(connection);
        }

        public Task<string?> SubscribeAsync(string deviceToken) =>
            _connection.InvokeAsync<string?>(nameof(PlayHub.Subscribe), deviceToken);

        public async Task<string> NextEventAsync()
        {
            using var timeout = new CancellationTokenSource(EventTimeout);
            return await _events.Reader.ReadAsync(timeout.Token);
        }

        public ValueTask DisposeAsync() => _connection.DisposeAsync();

        private void Record(string text) => _events.Writer.TryWrite(text);
    }

    private sealed class RecordingTeamNotifier : ITeamNotifier
    {
        private readonly ConcurrentQueue<string> _events = new();

        public IReadOnlyList<string> Events => [.. _events];

        public Task LockSolvedAsync(Guid teamId, Guid lockId) => Record($"LockSolved {teamId} {lockId}");

        public Task HintRevealedAsync(Guid teamId, Guid lockId, string hint) => Record($"HintRevealed {teamId} {lockId}");

        public Task DoorOpenedAsync(Guid teamId, Guid doorId) => Record($"DoorOpened {teamId} {doorId}");

        public Task TeamEscapedAsync(Guid teamId) => Record($"TeamEscaped {teamId}");

        public Task TeamChangedAsync(Guid participantId) => Record($"TeamChanged {participantId}");

        public Task SessionClosedAsync(IReadOnlyCollection<Guid> teamIds) =>
            Record($"SessionClosed {string.Join(' ', teamIds.Order())}");

        private Task Record(string text)
        {
            _events.Enqueue(text);
            return Task.CompletedTask;
        }
    }
}
