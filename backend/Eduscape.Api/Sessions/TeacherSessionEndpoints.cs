using System.Security.Claims;
using Eduscape.Api.Auth;
using Eduscape.Api.Play;
using Eduscape.Api.Teachers;
using Eduscape.Domain.Sessions;
using Eduscape.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Eduscape.Api.Sessions;

public sealed record SessionSummaryDto(
    Guid Id,
    string Pin,
    SessionMode Mode,
    SessionStatus Status,
    string RoomTitle,
    string ClassName,
    DateTimeOffset StartedAt,
    DateTimeOffset ExpiresAt);

public sealed record SessionParticipantDto(Guid Id, string? Nickname);

public sealed record SessionTeamDto(Guid Id, TeamColor Color, SessionParticipantDto[] Participants);

public sealed record SessionDetailsDto(
    Guid Id,
    string Pin,
    SessionMode Mode,
    SessionStatus Status,
    string RoomTitle,
    string ClassName,
    DateTimeOffset StartedAt,
    DateTimeOffset ExpiresAt,
    SessionTeamDto[] Teams);

public sealed record MoveParticipantRequest(Guid TeamId);

/// <summary>
/// Starting and running sessions. A session belongs to the teacher who owns its class; other teachers' sessions
/// are reported as not found.
/// </summary>
public static class TeacherSessionEndpoints
{
    public static IEndpointRouteBuilder MapTeacherSessionEndpoints(this IEndpointRouteBuilder app)
    {
        var teacher = app.MapGroup(TeacherEndpoints.RoutePrefix).RequireAuthorization();
        teacher.MapGet("/classes", GetClassNamesAsync);
        teacher.MapPost("/sessions", StartAsync);
        teacher.MapGet("/sessions", GetActiveSessionsAsync);
        teacher.MapGet("/sessions/{id:guid}", GetSessionAsync);
        teacher.MapPost("/sessions/{id:guid}/lock", LockAsync);
        teacher.MapPost("/sessions/{id:guid}/unlock", UnlockAsync);
        teacher.MapPost("/sessions/{id:guid}/close", CloseAsync);
        teacher.MapGet("/sessions/{id:guid}/dashboard", GetDashboardAsync);
        teacher.MapPost("/sessions/{id:guid}/participants/{participantId:guid}/move", MoveParticipantAsync);
        return app;
    }

    /// <summary>Names of the teacher's classes, suggested when starting a session.</summary>
    private static async Task<string[]> GetClassNamesAsync(
        ClaimsPrincipal user, EduscapeDbContext db, CancellationToken cancellationToken)
    {
        var teacherId = user.GetTeacherId();
        return await db.Classes
            .Where(c => c.OwnerTeacherId == teacherId)
            .OrderBy(c => c.Name)
            .Select(c => c.Name)
            .ToArrayAsync(cancellationToken);
    }

    private static async Task<Results<Created<SessionDetailsDto>, NotFound>> StartAsync(
        StartSessionRequest request,
        ClaimsPrincipal user,
        SessionStarter starter,
        EduscapeDbContext db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var teacherId = user.GetTeacherId();
        if (await starter.StartAsync(teacherId, request, cancellationToken) is not { } id)
        {
            return TypedResults.NotFound();
        }

        var details = await LoadDetailsAsync(id, teacherId, db, timeProvider, cancellationToken)
            ?? throw new InvalidOperationException("The new session was not found.");
        return TypedResults.Created($"{TeacherEndpoints.RoutePrefix}/sessions/{id}", details);
    }

    /// <summary>Sessions that can still be played (not closed, not expired), newest first.</summary>
    private static async Task<SessionSummaryDto[]> GetActiveSessionsAsync(
        ClaimsPrincipal user, EduscapeDbContext db, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var teacherId = user.GetTeacherId();
        var now = timeProvider.GetUtcNow();
        return await (
                from session in db.Sessions
                join schoolClass in db.Classes on session.ClassId equals schoolClass.Id
                join room in db.EscapeRooms on session.EscapeRoomId equals room.Id
                where schoolClass.OwnerTeacherId == teacherId && !session.IsPreview
                    && session.Status != SessionStatus.Closed
                    && session.ExpiresAt > now
                orderby session.StartedAt descending
                select new SessionSummaryDto(
                    session.Id,
                    session.Pin,
                    session.Mode,
                    session.Status,
                    room.Title,
                    schoolClass.Name,
                    session.StartedAt,
                    session.ExpiresAt))
            .ToArrayAsync(cancellationToken);
    }

    private static async Task<Results<Ok<SessionDetailsDto>, NotFound>> GetSessionAsync(
        Guid id, ClaimsPrincipal user, EduscapeDbContext db, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var details = await LoadDetailsAsync(id, user.GetTeacherId(), db, timeProvider, cancellationToken);
        return details is null ? TypedResults.NotFound() : TypedResults.Ok(details);
    }

    /// <summary>Stops new students from joining; students already in keep playing.</summary>
    private static Task<Results<NoContent, NotFound>> LockAsync(
        Guid id,
        ClaimsPrincipal user,
        EduscapeDbContext db,
        TimeProvider timeProvider,
        IDashboardNotifier dashboard,
        CancellationToken cancellationToken) =>
        ChangeAsync(id, user, db, timeProvider, dashboard, (session, _) => session.Lock(), cancellationToken);

    private static Task<Results<NoContent, NotFound>> UnlockAsync(
        Guid id,
        ClaimsPrincipal user,
        EduscapeDbContext db,
        TimeProvider timeProvider,
        IDashboardNotifier dashboard,
        CancellationToken cancellationToken) =>
        ChangeAsync(id, user, db, timeProvider, dashboard, (session, _) => session.Unlock(), cancellationToken);

    /// <summary>
    /// Ends the session: the PIN stops working at once and is released for other sessions. The students' data is
    /// erased shortly after; the teams' results stay without names.
    /// </summary>
    private static Task<Results<NoContent, NotFound>> CloseAsync(
        Guid id,
        ClaimsPrincipal user,
        EduscapeDbContext db,
        TimeProvider timeProvider,
        IDashboardNotifier dashboard,
        CancellationToken cancellationToken) =>
        ChangeAsync(id, user, db, timeProvider, dashboard, (session, now) => session.Close(now), cancellationToken);

    /// <summary>The live dashboard's state. Also works after the session closed, as its results.</summary>
    private static async Task<Results<Ok<SessionDashboardDto>, NotFound>> GetDashboardAsync(
        Guid id, ClaimsPrincipal user, EduscapeDbContext db, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var dashboard = await SessionDashboard.LoadAsync(id, user.GetTeacherId(), db, timeProvider, cancellationToken);
        return dashboard is null ? TypedResults.NotFound() : TypedResults.Ok(dashboard);
    }

    /// <summary>
    /// Moves a student to another team (team mode only). The student's devices are told to switch to the new team.
    /// </summary>
    private static async Task<Results<NoContent, NotFound>> MoveParticipantAsync(
        Guid id,
        Guid participantId,
        MoveParticipantRequest request,
        ClaimsPrincipal user,
        EduscapeDbContext db,
        TimeProvider timeProvider,
        ITeamNotifier teams,
        IDashboardNotifier dashboard,
        CancellationToken cancellationToken)
    {
        var session = await OwnSessions(db, user.GetTeacherId())
            .Include(s => s.Teams).ThenInclude(t => t.Participants)
            .AsSplitQuery()
            .SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (session is null)
        {
            return TypedResults.NotFound();
        }

        session.CloseIfExpired(timeProvider.GetUtcNow());
        session.MoveParticipant(participantId, request.TeamId);
        await db.SaveChangesAsync(cancellationToken);

        // Once saved, the student and the dashboard must hear of it, even if the request is cancelled.
        await teams.TeamChangedAsync(participantId);
        await dashboard.SessionChangedAsync(session.Id);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound>> ChangeAsync(
        Guid id,
        ClaimsPrincipal user,
        EduscapeDbContext db,
        TimeProvider timeProvider,
        IDashboardNotifier dashboard,
        Action<Session, DateTimeOffset> change,
        CancellationToken cancellationToken)
    {
        var session = await OwnSessions(db, user.GetTeacherId()).SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (session is null)
        {
            return TypedResults.NotFound();
        }

        // An expired session counts as closed, so it can no longer be locked or reopened.
        var now = timeProvider.GetUtcNow();
        session.CloseIfExpired(now);
        change(session, now);
        await db.SaveChangesAsync(cancellationToken);
        await dashboard.SessionChangedAsync(session.Id);
        return TypedResults.NoContent();
    }

    internal static IQueryable<Session> OwnSessions(EduscapeDbContext db, Guid teacherId) =>
        db.Sessions.Where(s => !s.IsPreview && db.Classes.Any(c => c.Id == s.ClassId && c.OwnerTeacherId == teacherId));

    private static async Task<SessionDetailsDto?> LoadDetailsAsync(
        Guid id, Guid teacherId, EduscapeDbContext db, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var session = await OwnSessions(db, teacherId)
            .Include(s => s.Teams).ThenInclude(t => t.Participants)
            .AsSplitQuery()
            .SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (session is null)
        {
            return null;
        }

        if (session.CloseIfExpired(timeProvider.GetUtcNow()))
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        var roomTitle = await db.EscapeRooms
            .Where(r => r.Id == session.EscapeRoomId)
            .Select(r => r.Title)
            .SingleAsync(cancellationToken);
        var className = await db.Classes
            .Where(c => c.Id == session.ClassId)
            .Select(c => c.Name)
            .SingleAsync(cancellationToken);
        var teams = session.Teams
            .OrderBy(t => t.Color)
            .ThenBy(t => t.Id)
            .Select(t => new SessionTeamDto(
                t.Id,
                t.Color,
                [.. t.Participants.OrderBy(p => p.JoinedAt).Select(p => new SessionParticipantDto(p.Id, p.Nickname))]))
            .ToArray();

        return new SessionDetailsDto(
            session.Id,
            session.Pin,
            session.Mode,
            session.Status,
            roomTitle,
            className,
            session.StartedAt,
            session.ExpiresAt,
            teams);
    }
}
