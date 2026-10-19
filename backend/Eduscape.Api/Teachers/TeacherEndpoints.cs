using System.Security.Claims;
using Eduscape.Api.Auth;
using Eduscape.Domain.Rooms;
using Eduscape.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Eduscape.Api.Teachers;

public sealed record RoomSummaryDto(Guid Id, string Title, Subject Subject, GradeLevel GradeLevel);

/// <summary>
/// Endpoints for signed-in teachers. Everything is mapped inside one group that requires authorization,
/// and every query is scoped to the signed-in teacher.
/// </summary>
public static class TeacherEndpoints
{
    public const string RoutePrefix = "/api/teacher";

    public static IEndpointRouteBuilder MapTeacherEndpoints(this IEndpointRouteBuilder app)
    {
        var teacher = app.MapGroup(RoutePrefix).RequireAuthorization();
        teacher.MapGet("/rooms", GetRoomsAsync);
        teacher.MapGet("/rooms/{id:guid}", GetRoomAsync);
        return app;
    }

    /// <summary>The teacher's own rooms. Templates (no owner) and other teachers' rooms are not included.</summary>
    private static async Task<RoomSummaryDto[]> GetRoomsAsync(
        ClaimsPrincipal user, EduscapeDbContext db, CancellationToken cancellationToken)
    {
        var teacherId = user.GetTeacherId();
        return await db.EscapeRooms
            .Where(r => r.OwnerTeacherId == teacherId)
            .OrderBy(r => r.Title)
            .Select(r => new RoomSummaryDto(r.Id, r.Title, r.Subject, r.GradeLevel))
            .ToArrayAsync(cancellationToken);
    }

    /// <summary>Another teacher's room is reported as not found, so its existence is not revealed.</summary>
    private static async Task<Results<Ok<RoomSummaryDto>, NotFound>> GetRoomAsync(
        Guid id, ClaimsPrincipal user, EduscapeDbContext db, CancellationToken cancellationToken)
    {
        var teacherId = user.GetTeacherId();
        var room = await db.EscapeRooms
            .Where(r => r.Id == id && r.OwnerTeacherId == teacherId)
            .Select(r => new RoomSummaryDto(r.Id, r.Title, r.Subject, r.GradeLevel))
            .SingleOrDefaultAsync(cancellationToken);
        return room is null ? TypedResults.NotFound() : TypedResults.Ok(room);
    }
}
