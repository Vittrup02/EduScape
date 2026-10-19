using Eduscape.Domain.Rooms;

namespace Eduscape.Api.Editor;

/// <summary>
/// An object as the teacher edits it, including the correct answer, tolerance and hint. Fields that do not apply to
/// the type are null. Never sent to students; they get <see cref="Play.PlayObjectDto"/>.
/// </summary>
public sealed record EditorObjectDto(
    Guid Id,
    RoomObjectType Type,
    double X,
    double Y,
    string GraphicKey,
    Guid? UnlockedById,
    string? Question = null,
    AnswerType? AnswerType = null,
    string? CorrectAnswer = null,
    decimal? Tolerance = null,
    string? Hint = null,
    string[]? Options = null,
    string? Text = null,
    Guid? TargetSceneId = null);

/// <summary><c>BackgroundImageUrl</c> is a short-lived link to an uploaded background; null for built-in ones.</summary>
public sealed record EditorSceneDto(
    Guid Id, string Name, string BackgroundImageKey, string? BackgroundImageUrl, EditorObjectDto[] Objects);

public sealed record RoomValidationErrorDto(string Code, Guid? ObjectId);

public sealed record EditorRoomDto(
    Guid Id,
    string Title,
    Subject Subject,
    GradeLevel GradeLevel,
    int Revision,
    Guid? StartSceneId,
    EditorSceneDto[] Scenes,
    RoomValidationErrorDto[] ValidationErrors,
    bool UploadsEnabled);

public sealed record SaveSceneRequest(Guid Id, string Name, string BackgroundImageKey, EditorObjectDto[] Objects);

/// <summary>
/// The whole room as the editor has it. Scenes and objects missing from it are removed; new ones keep the ids the
/// editor gave them. <c>Revision</c> is the revision the editor last loaded or saved.
/// </summary>
public sealed record SaveRoomRequest(
    int Revision,
    string Title,
    Subject Subject,
    GradeLevel GradeLevel,
    Guid StartSceneId,
    SaveSceneRequest[] Scenes);

public sealed record SaveRoomResponse(int Revision, RoomValidationErrorDto[] ValidationErrors);

/// <summary>A new room starts with one scene, named by the frontend in the teacher's language.</summary>
public sealed record CreateRoomRequest(string Title, Subject Subject, GradeLevel GradeLevel, string SceneName);

public sealed record PreviewResponse(string DeviceToken);
