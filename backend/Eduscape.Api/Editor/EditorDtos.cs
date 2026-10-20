using Eduscape.Domain.Rooms;

namespace Eduscape.Api.Editor;

/// <summary>
/// An item of an order step. <c>ImageUrl</c> is a short-lived link to the uploaded image (null without one, and
/// ignored when saving). The id is random (UUID v4), so its order says nothing about the correct order.
/// </summary>
public sealed record EditorOrderItemDto(Guid Id, string Text, string? ImageKey = null, string? ImageUrl = null);

/// <summary>
/// One step of a lock's puzzle as the teacher edits it, including the correct answer. Settings that don't apply to
/// the answer type are kept by the editor while the teacher switches type, and ignored when saving.
/// </summary>
public sealed record EditorStepDto(
    Guid Id,
    string Question,
    AnswerType AnswerType,
    string? CorrectAnswer = null,
    decimal? Tolerance = null,
    string? Hint = null,
    string[]? Options = null,
    string[]? AcceptedAnswers = null,
    bool AcceptTypos = false,
    EditorOrderItemDto[]? OrderItems = null);

/// <summary>
/// An object as the teacher edits it. A lock has its puzzle in <c>Steps</c> (empty until the teacher picks one), a
/// door or exit door its open condition. Fields that do not apply to the type are null. Never sent to students; they
/// get <see cref="Play.PlayObjectDto"/>.
/// </summary>
public sealed record EditorObjectDto(
    Guid Id,
    RoomObjectType Type,
    double X,
    double Y,
    string GraphicKey,
    Guid? UnlockedById,
    EditorStepDto[]? Steps = null,
    string? Text = null,
    Guid? TargetSceneId = null,
    DoorOpenCondition? OpenCondition = null,
    Guid[]? RequiredLockIds = null);

/// <summary>
/// <c>BackgroundImageUrl</c> is a short-lived link to an uploaded background; null for built-in ones.
/// <c>Narration</c> is the story text students see when they enter the scene; null when there is none.
/// </summary>
public sealed record EditorSceneDto(
    Guid Id,
    string Name,
    string BackgroundImageKey,
    string? BackgroundImageUrl,
    EditorObjectDto[] Objects,
    string? Narration = null);

/// <summary><c>StepIndex</c> says which step of a lock a problem is in; null otherwise.</summary>
public sealed record RoomValidationErrorDto(string Code, Guid? ObjectId, int? StepIndex = null);

public sealed record EditorRoomDto(
    Guid Id,
    string Title,
    Subject Subject,
    GradeLevel GradeLevel,
    int Revision,
    Guid? StartSceneId,
    EditorSceneDto[] Scenes,
    RoomValidationErrorDto[] ValidationErrors,
    bool UploadsEnabled,
    int WrongAnswerCooldownSeconds);

public sealed record SaveSceneRequest(
    Guid Id, string Name, string BackgroundImageKey, EditorObjectDto[] Objects, string? Narration = null);

/// <summary>
/// The whole room as the editor has it. Scenes and objects missing from it are removed; new ones keep the ids the
/// editor gave them, and the scenes keep the order they have here. <c>Revision</c> is the revision the editor last
/// loaded or saved.
/// </summary>
public sealed record SaveRoomRequest(
    int Revision,
    string Title,
    Subject Subject,
    GradeLevel GradeLevel,
    Guid StartSceneId,
    SaveSceneRequest[] Scenes,
    int WrongAnswerCooldownSeconds = EscapeRoom.DefaultWrongAnswerCooldownSeconds);

public sealed record SaveRoomResponse(int Revision, RoomValidationErrorDto[] ValidationErrors);

/// <summary>A new room starts with one scene, named by the frontend in the teacher's language.</summary>
public sealed record CreateRoomRequest(string Title, Subject Subject, GradeLevel GradeLevel, string SceneName);

/// <summary>Without <c>StartSceneId</c> the preview starts from the beginning.</summary>
public sealed record StartPreviewRequest(Guid? StartSceneId);

public sealed record PreviewResponse(string DeviceToken);
