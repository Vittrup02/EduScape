using System.Net;
using Eduscape.Api.Errors;
using Eduscape.Api.Sessions;
using Eduscape.Domain.Common;
using Eduscape.Domain.Sessions;
using Eduscape.Infrastructure.Persistence.Seeding;
using Eduscape.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Eduscape.Tests.Api;

public class SessionStartTests(EduscapeApiFactory factory) : IClassFixture<EduscapeApiFactory>
{
    [PostgresFact]
    public async Task Start_ForOwnRoom_CreatesTeamsAndPin()
    {
        var teacher = await factory.RegisterTeacherAsync();
        var room = await factory.AddRoomAsync(teacher.Id, "Brøker");

        var response = await SessionApi.StartAsync(teacher.Client, room.Id, "9.A", SessionMode.Team, 3);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var session = await SessionApi.ReadAsync<SessionDetailsDto>(response);
        Assert.Equal($"/api/teacher/sessions/{session.Id}", response.Headers.Location?.OriginalString);
        Assert.True(Session.IsValidPin(session.Pin), session.Pin);
        Assert.Equal(SessionMode.Team, session.Mode);
        Assert.Equal(SessionStatus.Open, session.Status);
        Assert.Equal("Brøker", session.RoomTitle);
        Assert.Equal("9.A", session.ClassName);
        Assert.Equal(TimeSpan.FromHours(12), session.ExpiresAt - session.StartedAt);
        Assert.Equal([TeamColor.Red, TeamColor.Blue, TeamColor.Green], session.Teams.Select(t => t.Color));
        Assert.All(session.Teams, team => Assert.Empty(team.Participants));
    }

    [PostgresFact]
    public async Task Start_ForTemplate_Works()
    {
        var teacher = await factory.RegisterTeacherAsync();

        var session = await SessionApi.StartSessionAsync(teacher.Client, DemoRoomSeeder.DemoRoomId);

        Assert.Equal(DemoRoomSeeder.BuildDemoRoom().Title, session.RoomTitle);
    }

    [PostgresFact]
    public async Task Start_InIndividualMode_HasNoTeams()
    {
        var teacher = await factory.RegisterTeacherAsync();

        var session = await SessionApi.StartSessionAsync(teacher.Client, DemoRoomSeeder.DemoRoomId, mode: SessionMode.Individual, teamCount: 0);

        Assert.Equal(SessionMode.Individual, session.Mode);
        Assert.Empty(session.Teams);
    }

    [PostgresFact]
    public async Task Start_ForAnotherTeachersRoom_IsNotFound()
    {
        var alice = await factory.RegisterTeacherAsync();
        var bob = await factory.RegisterTeacherAsync();
        var bobsRoom = await factory.AddRoomAsync(bob.Id, "Bobs rum");

        var response = await SessionApi.StartAsync(alice.Client, bobsRoom.Id);

        await TeacherAuthTests.AssertErrorAsync(response, HttpStatusCode.NotFound, ApiErrors.NotFound);
    }

    [PostgresFact]
    public async Task Start_WithInvalidTeamCount_IsRejected_WithoutCreatingTheClass()
    {
        var teacher = await factory.RegisterTeacherAsync();

        var response = await SessionApi.StartAsync(teacher.Client, DemoRoomSeeder.DemoRoomId, "9.A", SessionMode.Team, 9);

        await TeacherAuthTests.AssertErrorAsync(response, HttpStatusCode.BadRequest, DomainErrors.TeamCountInvalid);
        Assert.Empty(await SessionApi.ReadAsync<string[]>(await teacher.Client.GetAsync("/api/teacher/classes")));
    }

    [PostgresFact]
    public async Task Start_WithoutClassName_IsRejected()
    {
        var teacher = await factory.RegisterTeacherAsync();

        var response = await SessionApi.StartAsync(teacher.Client, DemoRoomSeeder.DemoRoomId, "   ");

        await TeacherAuthTests.AssertErrorAsync(response, HttpStatusCode.BadRequest, DomainErrors.ValueRequired);
    }

    [PostgresFact]
    public async Task Start_ReusesTheClass_IgnoringCaseAndSpaces()
    {
        var teacher = await factory.RegisterTeacherAsync();
        var other = await factory.RegisterTeacherAsync();

        await SessionApi.StartSessionAsync(teacher.Client, DemoRoomSeeder.DemoRoomId, "9.A");
        var second = await SessionApi.StartSessionAsync(teacher.Client, DemoRoomSeeder.DemoRoomId, " 9.a ");
        await SessionApi.StartSessionAsync(teacher.Client, DemoRoomSeeder.DemoRoomId, "8.B");
        await SessionApi.StartSessionAsync(other.Client, DemoRoomSeeder.DemoRoomId, "9.A");

        Assert.Equal("9.A", second.ClassName);
        var classes = await SessionApi.ReadAsync<string[]>(await teacher.Client.GetAsync("/api/teacher/classes"));
        Assert.Equal(["8.B", "9.A"], classes);
    }

    [PostgresFact]
    public async Task Start_SkipsPinsOfActiveSessions()
    {
        var pin = new RandomPinGenerator().Next();
        var app = factory.WithServices(services => services.AddSingleton<IPinGenerator>(new QueuedPinGenerator(pin, pin)));
        var teacher = await factory.RegisterTeacherAsync(app);

        var first = await SessionApi.StartSessionAsync(teacher.Client, DemoRoomSeeder.DemoRoomId);
        var second = await SessionApi.StartSessionAsync(teacher.Client, DemoRoomSeeder.DemoRoomId);

        Assert.Equal(pin, first.Pin);
        Assert.NotEqual(pin, second.Pin);
    }

    [PostgresFact]
    public async Task Start_ReusesThePinOfAClosedSession()
    {
        var pin = new RandomPinGenerator().Next();
        var app = factory.WithServices(services => services.AddSingleton<IPinGenerator>(new QueuedPinGenerator(pin, pin)));
        var teacher = await factory.RegisterTeacherAsync(app);
        var first = await SessionApi.StartSessionAsync(teacher.Client, DemoRoomSeeder.DemoRoomId);

        var close = await SessionApi.PostAsync(teacher.Client, first.Id, "close");
        var second = await SessionApi.StartSessionAsync(teacher.Client, DemoRoomSeeder.DemoRoomId);

        Assert.Equal(HttpStatusCode.NoContent, close.StatusCode);
        Assert.Equal(SessionStatus.Closed, (await SessionApi.GetSessionAsync(teacher.Client, first.Id)).Status);
        Assert.Equal(pin, second.Pin);
    }

    [PostgresFact]
    public async Task Start_ClosesExpiredSessions_AndReusesTheirPins()
    {
        var pin = new RandomPinGenerator().Next();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var app = factory.WithServices(services =>
        {
            services.AddSingleton<TimeProvider>(clock);
            services.AddSingleton<IPinGenerator>(new QueuedPinGenerator(pin, pin));
        });
        var teacher = await factory.RegisterTeacherAsync(app);
        var expired = await SessionApi.StartSessionAsync(teacher.Client, DemoRoomSeeder.DemoRoomId);

        clock.Advance(TimeSpan.FromHours(12));
        var later = await factory.LoginAsync(teacher.Email, app); // the access token from before has expired by now
        var reused = await SessionApi.StartSessionAsync(later, DemoRoomSeeder.DemoRoomId);

        Assert.Equal(pin, reused.Pin);
        await using var db = factory.Database.CreateContext();
        var closed = await db.Sessions.SingleAsync(s => s.Id == expired.Id);
        Assert.Equal(SessionStatus.Closed, closed.Status);
        Assert.Equal(closed.ExpiresAt, closed.ClosedAt);
    }

    [PostgresFact]
    public async Task ActiveSessions_ListsOnlyTheTeachersOpenSessions()
    {
        var teacher = await factory.RegisterTeacherAsync();
        var other = await factory.RegisterTeacherAsync();
        var closed = await SessionApi.StartSessionAsync(teacher.Client, DemoRoomSeeder.DemoRoomId, "9.A");
        var locked = await SessionApi.StartSessionAsync(teacher.Client, DemoRoomSeeder.DemoRoomId, "9.B");
        var open = await SessionApi.StartSessionAsync(teacher.Client, DemoRoomSeeder.DemoRoomId, "9.C");
        await SessionApi.StartSessionAsync(other.Client, DemoRoomSeeder.DemoRoomId);
        await SessionApi.PostAsync(teacher.Client, closed.Id, "close");
        await SessionApi.PostAsync(teacher.Client, locked.Id, "lock");

        var sessions = await SessionApi.GetActiveSessionsAsync(teacher.Client);

        // Newest first; locked sessions are still running.
        Assert.Equal([open.Id, locked.Id], sessions.Select(s => s.Id));
        Assert.Equal(SessionStatus.Locked, sessions[1].Status);
        Assert.Equal("9.C", sessions[0].ClassName);
        Assert.Equal(open.Pin, sessions[0].Pin);
    }

    [PostgresFact]
    public async Task ActiveSessions_LeaveOutExpiredSessions()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var app = factory.WithServices(services => services.AddSingleton<TimeProvider>(clock));
        var teacher = await factory.RegisterTeacherAsync(app);
        await SessionApi.StartSessionAsync(teacher.Client, DemoRoomSeeder.DemoRoomId);

        clock.Advance(TimeSpan.FromHours(12));

        // Signed in again: the access token from before has expired at the new time.
        var client = await factory.LoginAsync(teacher.Email, app);
        Assert.Empty(await SessionApi.GetActiveSessionsAsync(client));
    }

    [PostgresFact]
    public async Task LockAndUnlock_ChangeTheStatus()
    {
        var teacher = await factory.RegisterTeacherAsync();
        var session = await SessionApi.StartSessionAsync(teacher.Client, DemoRoomSeeder.DemoRoomId);

        await SessionApi.PostAsync(teacher.Client, session.Id, "lock");
        Assert.Equal(SessionStatus.Locked, (await SessionApi.GetSessionAsync(teacher.Client, session.Id)).Status);

        await SessionApi.PostAsync(teacher.Client, session.Id, "unlock");
        Assert.Equal(SessionStatus.Open, (await SessionApi.GetSessionAsync(teacher.Client, session.Id)).Status);
    }

    [PostgresFact]
    public async Task Close_IsFinal()
    {
        var teacher = await factory.RegisterTeacherAsync();
        var session = await SessionApi.StartSessionAsync(teacher.Client, DemoRoomSeeder.DemoRoomId);
        await SessionApi.PostAsync(teacher.Client, session.Id, "close");

        var closeAgain = await SessionApi.PostAsync(teacher.Client, session.Id, "close");
        var reopen = await SessionApi.PostAsync(teacher.Client, session.Id, "unlock");

        Assert.Equal(HttpStatusCode.NoContent, closeAgain.StatusCode);
        await TeacherAuthTests.AssertErrorAsync(reopen, HttpStatusCode.BadRequest, DomainErrors.SessionClosed);
    }

    [PostgresFact]
    public async Task AnotherTeachersSession_IsNotFound_AndCannotBeClosed()
    {
        var alice = await factory.RegisterTeacherAsync();
        var bob = await factory.RegisterTeacherAsync();
        var session = await SessionApi.StartSessionAsync(alice.Client, DemoRoomSeeder.DemoRoomId);

        var get = await bob.Client.GetAsync($"/api/teacher/sessions/{session.Id}");
        var close = await SessionApi.PostAsync(bob.Client, session.Id, "close");

        await TeacherAuthTests.AssertErrorAsync(get, HttpStatusCode.NotFound, ApiErrors.NotFound);
        await TeacherAuthTests.AssertErrorAsync(close, HttpStatusCode.NotFound, ApiErrors.NotFound);
        Assert.Equal(SessionStatus.Open, (await SessionApi.GetSessionAsync(alice.Client, session.Id)).Status);
    }
}
