using System.Security.Claims;
using Eduscape.Api.Auth;
using Eduscape.Api.Errors;
using Eduscape.Api.Sessions;
using Eduscape.Api.Teachers;
using Eduscape.Api.Uploads;
using Eduscape.Domain.Rooms;
using Eduscape.Infrastructure.Persistence;
using Eduscape.Infrastructure.Storage;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Eduscape.Api.Editor;

/// <summary>
/// The room editor. Teachers edit only their own rooms; a template is copied first and the copy is edited, in the same
/// editor. Other teachers' rooms and templates are reported as not found.
/// </summary>
public static class EditorEndpoints
{
    public const string DefaultBackground = "builtin:classroom";

    public static IEndpointRouteBuilder MapEditorEndpoints(this IEndpointRouteBuilder app)
    {
        var teacher = app.MapGroup(TeacherEndpoints.RoutePrefix).RequireAuthorization();
        teacher.MapPost("/rooms", CreateRoomAsync);
        teacher.MapGet("/rooms/{id:guid}/editor", GetEditorRoomAsync);
        teacher.MapPut("/rooms/{id:guid}/editor", SaveEditorRoomAsync);
        teacher.MapPost("/rooms/{id:guid}/preview", StartPreviewAsync);
        teacher.MapPost("/templates/{id:guid}/copy", CopyTemplateAsync);
        return app;
    }

    private static async Task<Created<RoomSummaryDto>> CreateRoomAsync(
        CreateRoomRequest request, ClaimsPrincipal user, EduscapeDbContext db, CancellationToken cancellationToken)
    {
        var room = new EscapeRoom(request.Title, request.Subject, request.GradeLevel, user.GetTeacherId());
        room.AddScene(request.SceneName, DefaultBackground);
        db.EscapeRooms.Add(room);
        await db.SaveChangesAsync(cancellationToken);
        return Created(room);
    }

    private static async Task<Results<Created<RoomSummaryDto>, NotFound>> CopyTemplateAsync(
        Guid id, ClaimsPrincipal user, EduscapeDbContext db, CancellationToken cancellationToken)
    {
        var template = await LoadRooms(db.EscapeRooms.AsNoTracking())
            .SingleOrDefaultAsync(r => r.Id == id && r.IsTemplate, cancellationToken);
        if (template is null)
        {
            return TypedResults.NotFound();
        }

        var copy = template.CopyFor(user.GetTeacherId());
        db.EscapeRooms.Add(copy);
        await db.SaveChangesAsync(cancellationToken);
        return Created(copy);
    }

    private static async Task<Results<Ok<EditorRoomDto>, NotFound>> GetEditorRoomAsync(
        Guid id,
        ClaimsPrincipal user,
        EduscapeDbContext db,
        ImageUrls imageUrls,
        IImageStorage storage,
        CancellationToken cancellationToken)
    {
        var room = await LoadOwnRoomAsync(db.EscapeRooms.AsNoTracking(), id, user, cancellationToken);
        return room is null ? TypedResults.NotFound() : TypedResults.Ok(ToDto(room, imageUrls, storage));
    }

    private static async Task<IResult> SaveEditorRoomAsync(
        Guid id, SaveRoomRequest request, ClaimsPrincipal user, EduscapeDbContext db, CancellationToken cancellationToken)
    {
        var room = await LoadOwnRoomAsync(db.EscapeRooms, id, user, cancellationToken);
        if (room is null)
        {
            return TypedResults.NotFound();
        }

        if (request.Revision != room.Revision)
        {
            return Conflict(ApiErrors.RoomChanged);
        }

        // Ids the editor made up must not belong to anything else, e.g. a scene in another teacher's room.
        var newIds = RoomDocumentApplier.NewIds(room, request);
        if (newIds.Count > 0
            && (await db.Scenes.AnyAsync(s => newIds.Contains(s.Id), cancellationToken)
                || await db.RoomObjects.AnyAsync(o => newIds.Contains(o.Id), cancellationToken)))
        {
            return Conflict(ApiErrors.IdTaken);
        }

        var startSceneBefore = room.StartScene?.Id;
        RoomDocumentApplier.Apply(room, request);

        try
        {
            await SaveAsync(db, room, startSceneBefore, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another save of the same room won the race.
            return Conflict(ApiErrors.RoomChanged);
        }

        return TypedResults.Ok(new SaveRoomResponse(room.Revision, ValidationErrors(room)));
    }

    /// <summary>
    /// At most one scene per room may be the start scene, and PostgreSQL checks that for every statement. When the start
    /// scene changes, the old one is cleared in a statement of its own, all in one transaction.
    /// </summary>
    private static async Task SaveAsync(
        EduscapeDbContext db, EscapeRoom room, Guid? startSceneBefore, CancellationToken cancellationToken)
    {
        var newStart = room.StartScene;
        if (startSceneBefore is null || newStart is null || newStart.Id == startSceneBefore)
        {
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Scenes
            .Where(s => s.Id == startSceneBefore)
            .ExecuteUpdateAsync(update => update.SetProperty(s => s.IsStartScene, false), cancellationToken);

        // A new scene is inserted as the start scene; an existing one is switched after everything else is saved.
        var isNewScene = db.Entry(newStart).State == EntityState.Added;
        if (!isNewScene)
        {
            db.Entry(newStart).Property(s => s.IsStartScene).IsModified = false;
        }

        await db.SaveChangesAsync(cancellationToken);
        if (!isNewScene)
        {
            await db.Scenes
                .Where(s => s.Id == newStart.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(s => s.IsStartScene, true), cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<IResult> StartPreviewAsync(
        Guid id, ClaimsPrincipal user, SessionStarter starter, CancellationToken cancellationToken)
    {
        var deviceToken = await starter.StartPreviewAsync(user.GetTeacherId(), id, cancellationToken);
        return deviceToken is null ? TypedResults.NotFound() : TypedResults.Ok(new PreviewResponse(deviceToken));
    }

    private static Task<EscapeRoom?> LoadOwnRoomAsync(
        IQueryable<EscapeRoom> rooms, Guid id, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var teacherId = user.GetTeacherId();
        return LoadRooms(rooms).SingleOrDefaultAsync(r => r.Id == id && r.OwnerTeacherId == teacherId, cancellationToken);
    }

    private static IQueryable<EscapeRoom> LoadRooms(IQueryable<EscapeRoom> rooms) =>
        rooms.Include(r => r.Scenes).ThenInclude(s => s.Objects).AsSplitQuery();

    private static Created<RoomSummaryDto> Created(EscapeRoom room) =>
        TypedResults.Created(
            $"{TeacherEndpoints.RoutePrefix}/rooms/{room.Id}",
            new RoomSummaryDto(room.Id, room.Title, room.Subject, room.GradeLevel));

    private static IResult Conflict(string code) =>
        TypedResults.Json(new ApiError(code), statusCode: StatusCodes.Status409Conflict);

    private static RoomValidationErrorDto[] ValidationErrors(EscapeRoom room) =>
        [.. room.Validate().Select(e => new RoomValidationErrorDto(e.Code, e.ObjectId))];

    private static EditorRoomDto ToDto(EscapeRoom room, ImageUrls imageUrls, IImageStorage storage) => new(
        room.Id,
        room.Title,
        room.Subject,
        room.GradeLevel,
        room.Revision,
        room.StartScene?.Id,
        [.. room.Scenes.OrderBy(s => s.SortOrder).Select(scene => new EditorSceneDto(
            scene.Id,
            scene.Name,
            scene.BackgroundImageKey,
            imageUrls.For(scene.BackgroundImageKey),
            [.. scene.Objects.OrderBy(o => o.Id).Select(ToDto)],
            scene.Narration))],
        ValidationErrors(room),
        storage.IsConfigured);

    private static EditorObjectDto ToDto(RoomObject roomObject)
    {
        var dto = new EditorObjectDto(
            roomObject.Id,
            roomObject.Type,
            roomObject.Position.X,
            roomObject.Position.Y,
            roomObject.GraphicKey,
            roomObject.UnlockedById);

        return roomObject switch
        {
            Lock @lock => dto with
            {
                Question = @lock.Question,
                AnswerType = @lock.AnswerType,
                CorrectAnswer = @lock.CorrectAnswer,
                Tolerance = @lock.Tolerance,
                Hint = @lock.Hint,
                Options = @lock.Options,
            },
            Letter letter => dto with { Text = letter.Text },
            Door door => dto with { TargetSceneId = door.TargetSceneId },
            _ => dto,
        };
    }
}
