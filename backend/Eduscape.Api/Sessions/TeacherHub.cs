using Eduscape.Api.Auth;
using Eduscape.Api.Errors;
using Eduscape.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Eduscape.Api.Sessions;

/// <summary>Events the server pushes to a teacher's dashboard.</summary>
public interface ITeacherClient
{
    /// <summary>
    /// Something in the session changed: a student joined, answered, moved or changed scene. Carries no data; the
    /// dashboard fetches the session's whole state again, the same way after every reconnect.
    /// </summary>
    Task DashboardChanged();
}

/// <summary>
/// Realtime channel for the teacher's live dashboard. Each watched session is a SignalR group that only the
/// teacher who owns the session can join.
/// </summary>
[Authorize]
public sealed class TeacherHub(EduscapeDbContext db) : Hub<ITeacherClient>
{
    public const string Path = "/hubs/teacher";

    public static string DashboardGroup(Guid sessionId) => $"dashboard:{sessionId}";

    /// <summary>
    /// Adds this connection to the session's dashboard group. Returns null on success, or
    /// <see cref="ApiErrors.NotFound"/> for a session that is not the teacher's. Clients call it again after every
    /// reconnect.
    /// </summary>
    public async Task<string?> Watch(Guid sessionId)
    {
        var teacherId = Context.User!.GetTeacherId();
        if (!await TeacherSessionEndpoints.OwnSessions(db, teacherId).AnyAsync(s => s.Id == sessionId, Context.ConnectionAborted))
        {
            return ApiErrors.NotFound;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, DashboardGroup(sessionId), Context.ConnectionAborted);
        return null;
    }
}

/// <summary>Tells the teacher's dashboard that a session changed. An interface so tests can count the events.</summary>
public interface IDashboardNotifier
{
    Task SessionChangedAsync(Guid sessionId);
}

internal sealed class SignalRDashboardNotifier(IHubContext<TeacherHub, ITeacherClient> hub) : IDashboardNotifier
{
    public Task SessionChangedAsync(Guid sessionId) =>
        hub.Clients.Group(TeacherHub.DashboardGroup(sessionId)).DashboardChanged();
}
