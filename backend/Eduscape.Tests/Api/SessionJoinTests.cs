using System.Net;
using Eduscape.Api.Errors;
using Eduscape.Api.Play;
using Eduscape.Api.Sessions;
using Eduscape.Domain.Common;
using Eduscape.Domain.Rooms;
using Eduscape.Domain.Sessions;
using Eduscape.Infrastructure.Persistence.Seeding;
using Eduscape.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Eduscape.Tests.Api;

public class SessionJoinTests(EduscapeApiFactory factory) : IClassFixture<EduscapeApiFactory>
{
    private HttpClient Student => factory.CreateClient();

    [PostgresFact]
    public async Task Lookup_InTeamMode_ListsTheTeams()
    {
        var (_, session) = await StartAsync(SessionMode.Team, teamCount: 3);

        var lookup = await SessionApi.ReadAsync<LookupResponse>(await SessionApi.LookupAsync(Student, session.Pin));

        Assert.Equal(SessionMode.Team, lookup.Mode);
        Assert.Equal(session.Teams.Select(t => (t.Id, t.Color)), lookup.Teams.Select(t => (t.Id, t.Color)));
    }

    [PostgresFact]
    public async Task Lookup_InIndividualMode_ListsNoTeams()
    {
        var (_, session) = await StartAsync(SessionMode.Individual);

        var lookup = await SessionApi.ReadAsync<LookupResponse>(await SessionApi.LookupAsync(Student, $" {session.Pin} "));

        Assert.Equal(SessionMode.Individual, lookup.Mode);
        Assert.Empty(lookup.Teams);
    }

    [PostgresFact]
    public async Task Lookup_WithUnknownPin_ReturnsPinNotFound()
    {
        var pin = await UnusedPinAsync();

        var response = await SessionApi.LookupAsync(Student, pin);

        await TeacherAuthTests.AssertErrorAsync(response, HttpStatusCode.NotFound, ApiErrors.PinNotFound);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("12 345")]
    [InlineData("abcdef")]
    public async Task LookupAndJoin_WithMalformedPin_ReturnPinInvalid(string? pin)
    {
        var lookup = await SessionApi.LookupAsync(Student, pin);
        var join = await SessionApi.JoinAsync(Student, pin, null, "Ida");

        await TeacherAuthTests.AssertErrorAsync(lookup, HttpStatusCode.BadRequest, DomainErrors.SessionPinInvalid);
        await TeacherAuthTests.AssertErrorAsync(join, HttpStatusCode.BadRequest, DomainErrors.SessionPinInvalid);
    }

    [PostgresFact]
    public async Task Join_InTeamMode_JoinsTheChosenTeam_AndStoresOnlyTheTokenHash()
    {
        var (teacher, session) = await StartAsync(SessionMode.Team, teamCount: 3);
        var blue = session.Teams.Single(t => t.Color == TeamColor.Blue);

        var joined = await SessionApi.JoinedAsync(Student, session.Pin, blue.Id, " Ida ");

        Assert.Equal(session.Id, joined.State.SessionId);
        Assert.Equal(blue.Id, joined.State.Team.Id);
        Assert.Equal(TeamColor.Blue, joined.State.Team.Color);
        Assert.Equal("Ida", joined.State.Nickname);
        Assert.Equal(SessionStatus.Open, joined.State.Status);
        Assert.Empty(joined.State.Team.SolvedLockIds);

        await using (var db = factory.Database.CreateContext())
        {
            var participant = await db.Participants.SingleAsync(p => p.Id == joined.State.ParticipantId);
            Assert.Equal(DeviceToken.Hash(joined.DeviceToken), participant.DeviceTokenHash);
            Assert.False(await db.Participants.AnyAsync(p => p.DeviceTokenHash == joined.DeviceToken));
        }

        var details = await SessionApi.GetSessionAsync(teacher.Client, session.Id);
        var ida = Assert.Single(details.Teams.Single(t => t.Id == blue.Id).Participants);
        Assert.Equal("Ida", ida.Nickname);
    }

    [PostgresFact]
    public async Task Join_InIndividualMode_GivesEveryStudentTheirOwnTeam()
    {
        var (teacher, session) = await StartAsync(SessionMode.Individual);

        var ida = await SessionApi.JoinedAsync(Student, session.Pin, null, "Ida");
        var bo = await SessionApi.JoinedAsync(Student, session.Pin, Guid.NewGuid(), "Bo");

        Assert.NotEqual(ida.State.Team.Id, bo.State.Team.Id);
        var details = await SessionApi.GetSessionAsync(teacher.Client, session.Id);
        Assert.Equal(2, details.Teams.Length);
        Assert.All(details.Teams, team => Assert.Single(team.Participants));
    }

    [PostgresFact]
    public async Task Join_WithoutNickname_Works()
    {
        var (_, session) = await StartAsync(SessionMode.Team);

        var joined = await SessionApi.JoinedAsync(Student, session.Pin, session.Teams[0].Id, nickname: null);

        Assert.Null(joined.State.Nickname);
    }

    [PostgresFact]
    public async Task Join_WithTooLongNickname_IsRejected()
    {
        var (_, session) = await StartAsync(SessionMode.Team);

        var response = await SessionApi.JoinAsync(Student, session.Pin, session.Teams[0].Id, new string('x', TextLimits.Nickname + 1));

        await TeacherAuthTests.AssertErrorAsync(response, HttpStatusCode.BadRequest, DomainErrors.ValueTooLong);
    }

    [PostgresFact]
    public async Task Join_InTeamMode_WithATeamOfAnotherSession_IsRejected()
    {
        var (_, session) = await StartAsync(SessionMode.Team);
        var (_, other) = await StartAsync(SessionMode.Team);

        var response = await SessionApi.JoinAsync(Student, session.Pin, other.Teams[0].Id, "Ida");

        await TeacherAuthTests.AssertErrorAsync(response, HttpStatusCode.BadRequest, DomainErrors.TeamNotFound);
    }

    [PostgresFact]
    public async Task Reconnect_ReturnsTheSameTeam_WithTheWholeTeamsProgress()
    {
        var (_, session) = await StartAsync(SessionMode.Team);
        var red = session.Teams[0];
        var ida = await SessionApi.JoinedAsync(Student, session.Pin, red.Id, "Ida");
        var bo = await SessionApi.JoinedAsync(Student, session.Pin, red.Id, "Bo");
        var lockId = await SolveFirstDemoLockAsync(red.Id);

        var reconnected = await SessionApi.ReadAsync<PlayStateDto>(await SessionApi.ReconnectAsync(Student, ida.DeviceToken));

        Assert.Equal(ida.State.ParticipantId, reconnected.ParticipantId);
        Assert.Equal("Ida", reconnected.Nickname);
        Assert.Equal(red.Id, reconnected.Team.Id);
        Assert.Equal([lockId], reconnected.Team.SolvedLockIds);

        // Progress is shared by the team: Bo sees Ida's solved lock too.
        var boState = await SessionApi.ReadAsync<PlayStateDto>(await SessionApi.ReconnectAsync(Student, bo.DeviceToken));
        Assert.Equal([lockId], boState.Team.SolvedLockIds);
        Assert.NotEqual(ida.DeviceToken, bo.DeviceToken);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-device-token")]
    public async Task Reconnect_WithUnknownToken_ReturnsDeviceNotRecognized(string? token)
    {
        if (token is { Length: > 0 } && PostgresDatabaseFixture.ServerConnectionString is null)
        {
            return; // looking the token up needs the database
        }

        var response = await SessionApi.ReconnectAsync(Student, token);

        await TeacherAuthTests.AssertErrorAsync(response, HttpStatusCode.NotFound, ApiErrors.DeviceNotRecognized);
    }

    [PostgresFact]
    public async Task AfterClose_ThePinStopsWorking_AndReconnectReportsTheSessionClosed()
    {
        var (teacher, session) = await StartAsync(SessionMode.Team);
        var ida = await SessionApi.JoinedAsync(Student, session.Pin, session.Teams[0].Id, "Ida");

        await SessionApi.PostAsync(teacher.Client, session.Id, "close");

        await TeacherAuthTests.AssertErrorAsync(
            await SessionApi.LookupAsync(Student, session.Pin), HttpStatusCode.NotFound, ApiErrors.PinNotFound);
        await TeacherAuthTests.AssertErrorAsync(
            await SessionApi.JoinAsync(Student, session.Pin, session.Teams[0].Id, "Bo"), HttpStatusCode.NotFound, ApiErrors.PinNotFound);
        await TeacherAuthTests.AssertErrorAsync(
            await SessionApi.ReconnectAsync(Student, ida.DeviceToken), HttpStatusCode.BadRequest, DomainErrors.SessionClosed);
    }

    [PostgresFact]
    public async Task WhenLocked_NewStudentsAreTurnedAway_ButJoinedStudentsKeepPlaying()
    {
        var (teacher, session) = await StartAsync(SessionMode.Team);
        var ida = await SessionApi.JoinedAsync(Student, session.Pin, session.Teams[0].Id, "Ida");

        await SessionApi.PostAsync(teacher.Client, session.Id, "lock");

        await TeacherAuthTests.AssertErrorAsync(
            await SessionApi.LookupAsync(Student, session.Pin), HttpStatusCode.BadRequest, DomainErrors.SessionLocked);
        await TeacherAuthTests.AssertErrorAsync(
            await SessionApi.JoinAsync(Student, session.Pin, session.Teams[0].Id, "Bo"), HttpStatusCode.BadRequest, DomainErrors.SessionLocked);
        var state = await SessionApi.ReadAsync<PlayStateDto>(await SessionApi.ReconnectAsync(Student, ida.DeviceToken));
        Assert.Equal(SessionStatus.Locked, state.Status);

        await SessionApi.PostAsync(teacher.Client, session.Id, "unlock");
        await SessionApi.JoinedAsync(Student, session.Pin, session.Teams[0].Id, "Bo");
    }

    [PostgresFact]
    public async Task AfterExpiry_ThePinIsExpired_AndTheSessionCloses()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var app = factory.WithServices(services => services.AddSingleton<TimeProvider>(clock));
        var teacher = await factory.RegisterTeacherAsync(app);
        var session = await SessionApi.StartSessionAsync(teacher.Client, DemoRoomSeeder.DemoRoomId);
        var student = app.CreateClient();
        var ida = await SessionApi.JoinedAsync(student, session.Pin, session.Teams[0].Id, "Ida");

        clock.Advance(TimeSpan.FromHours(12));

        await TeacherAuthTests.AssertErrorAsync(
            await SessionApi.LookupAsync(student, session.Pin), HttpStatusCode.BadRequest, DomainErrors.PinExpired);
        await TeacherAuthTests.AssertErrorAsync(
            await SessionApi.JoinAsync(student, session.Pin, session.Teams[0].Id, "Bo"), HttpStatusCode.BadRequest, DomainErrors.PinExpired);
        await TeacherAuthTests.AssertErrorAsync(
            await SessionApi.ReconnectAsync(student, ida.DeviceToken), HttpStatusCode.BadRequest, DomainErrors.SessionClosed);

        await using var db = factory.Database.CreateContext();
        var closed = await db.Sessions.SingleAsync(s => s.Id == session.Id);
        Assert.Equal(SessionStatus.Closed, closed.Status);
        Assert.Equal(closed.ExpiresAt, closed.ClosedAt);
    }

    private async Task<(SignedInTeacher Teacher, SessionDetailsDto Session)> StartAsync(SessionMode mode, int teamCount = 2)
    {
        var teacher = await factory.RegisterTeacherAsync();
        var session = await SessionApi.StartSessionAsync(teacher.Client, DemoRoomSeeder.DemoRoomId, "9.A", mode, teamCount);
        return (teacher, session);
    }

    private async Task<string> UnusedPinAsync()
    {
        await using var db = factory.Database.CreateContext();
        var generator = new RandomPinGenerator();
        while (true)
        {
            var pin = generator.Next();
            if (!await db.Sessions.AnyAsync(s => s.Pin == pin))
            {
                return pin;
            }
        }
    }

    /// <summary>Solves a lock of the demo room for the team, as if a team member had answered it.</summary>
    private async Task<Guid> SolveFirstDemoLockAsync(Guid teamId)
    {
        await using var db = factory.Database.CreateContext();
        var lockId = await db.RoomObjects
            .OfType<Lock>()
            .Where(l => db.Scenes.Any(s => s.Id == l.SceneId && s.EscapeRoomId == DemoRoomSeeder.DemoRoomId))
            .Select(l => l.Id)
            .FirstAsync();
        var team = await db.Teams.Include(t => t.LockProgress).SingleAsync(t => t.Id == teamId);
        team.TrySolveLock(lockId, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
        return lockId;
    }
}
