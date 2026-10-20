using Eduscape.Api.Sessions;
using Eduscape.Domain.Sessions;
using Eduscape.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Eduscape.Tests.Api;

/// <summary>Students' data is erased an hour after the session closes; the teams' results stay without names.</summary>
public class StudentDataCleanupTests(EduscapeApiFactory factory) : IClassFixture<EduscapeApiFactory>
{
    private static readonly TimeSpan Retention = new SessionOptions().StudentDataRetention;

    [PostgresFact]
    public async Task ClosedSession_KeepsStudentDataDuringTheRetention()
    {
        var (session, _) = await StartClosedSessionWithIdaAsync();
        var closedAt = await ClosedAtAsync(session.Id);

        await RunCleanupAsync(closedAt + Retention - TimeSpan.FromMinutes(1));

        await using var db = factory.Database.CreateContext();
        Assert.True(await db.Participants.AnyAsync(p => db.Teams.Any(t => t.Id == p.TeamId && t.SessionId == session.Id)));
    }

    [PostgresFact]
    public async Task ClosedSession_AfterTheRetention_KeepsOnlyTheResultsWithoutNames()
    {
        var (session, room) = await StartClosedSessionWithIdaAsync();
        var closedAt = await ClosedAtAsync(session.Id);

        await RunCleanupAsync(closedAt + Retention);

        await using var db = factory.Database.CreateContext();
        Assert.False(await db.Participants.AnyAsync(p => db.Teams.Any(t => t.Id == p.TeamId && t.SessionId == session.Id)));
        var red = await db.Teams.Include(t => t.LockProgress).SingleAsync(t => t.Id == session.Teams[0].Id);
        Assert.Equal(1, red.ParticipantCount);
        Assert.True(red.IsLockSolved(room.CodeLock.Id));
    }

    [PostgresFact]
    public async Task OpenSession_IsLeftAlone()
    {
        var teacher = await factory.RegisterTeacherAsync();
        var room = await PlayTestRoom.AddAsync(factory, teacher.Id);
        var session = await SessionApi.StartSessionAsync(teacher.Client, room.Room.Id);
        await SessionApi.JoinedAsync(factory.CreateClient(), session.Pin, session.Teams[0].Id, "Ida");

        await RunCleanupAsync(session.StartedAt + TimeSpan.FromHours(1));

        await using var db = factory.Database.CreateContext();
        Assert.Equal(SessionStatus.Open, (await db.Sessions.SingleAsync(s => s.Id == session.Id)).Status);
        Assert.True(await db.Participants.AnyAsync(p => p.TeamId == session.Teams[0].Id));
    }

    [PostgresFact]
    public async Task ExpiredSession_IsClosedAndErased()
    {
        var teacher = await factory.RegisterTeacherAsync();
        var room = await PlayTestRoom.AddAsync(factory, teacher.Id);
        var session = await SessionApi.StartSessionAsync(teacher.Client, room.Room.Id);
        await SessionApi.JoinedAsync(factory.CreateClient(), session.Pin, session.Teams[0].Id, "Ida");

        await RunCleanupAsync(session.ExpiresAt + Retention);

        await using var db = factory.Database.CreateContext();
        var closed = await db.Sessions.SingleAsync(s => s.Id == session.Id);
        Assert.Equal(SessionStatus.Closed, closed.Status);
        Assert.Equal(closed.ExpiresAt, closed.ClosedAt);
        Assert.False(await db.Participants.AnyAsync(p => p.TeamId == session.Teams[0].Id));
    }

    [PostgresFact]
    public async Task Dashboard_AfterCleanup_ShowsHowManyPlayed_ButNoNames()
    {
        var teacher = await factory.RegisterTeacherAsync();
        var room = await PlayTestRoom.AddAsync(factory, teacher.Id);
        var session = await SessionApi.StartSessionAsync(teacher.Client, room.Room.Id);
        await SessionApi.JoinedAsync(factory.CreateClient(), session.Pin, session.Teams[0].Id, "Ida");
        (await SessionApi.PostAsync(teacher.Client, session.Id, "close")).EnsureSuccessStatusCode();

        await RunCleanupAsync(await ClosedAtAsync(session.Id) + Retention);
        var dashboard = await SessionApi.GetDashboardAsync(teacher.Client, session.Id);

        var red = dashboard.Teams.Single(t => t.Id == session.Teams[0].Id);
        Assert.Empty(red.Participants);
        Assert.Equal(1, red.ParticipantCount);
    }

    private async Task<(SessionDetailsDto Session, PlayTestRoom Room)> StartClosedSessionWithIdaAsync()
    {
        var teacher = await factory.RegisterTeacherAsync();
        var room = await PlayTestRoom.AddAsync(factory, teacher.Id);
        var session = await SessionApi.StartSessionAsync(teacher.Client, room.Room.Id);
        var ida = await SessionApi.JoinedAsync(factory.CreateClient(), session.Pin, session.Teams[0].Id, "Ida");
        await SessionApi.AnsweredAsync(factory.CreateClient(), ida.DeviceToken, room.CodeLock.Id, PlayTestRoom.CodeAnswer);
        (await SessionApi.PostAsync(teacher.Client, session.Id, "close")).EnsureSuccessStatusCode();
        return (session, room);
    }

    private async Task<DateTimeOffset> ClosedAtAsync(Guid sessionId)
    {
        await using var db = factory.Database.CreateContext();
        return (await db.Sessions.SingleAsync(s => s.Id == sessionId)).ClosedAt
            ?? throw new InvalidOperationException("The session is not closed.");
    }

    private async Task RunCleanupAsync(DateTimeOffset now)
    {
        await using var db = factory.Database.CreateContext();
        var cleanup = new StudentDataCleanup(db, new FakeTimeProvider(now), Options.Create(new SessionOptions()));
        await cleanup.RunOnceAsync(CancellationToken.None);
    }
}
