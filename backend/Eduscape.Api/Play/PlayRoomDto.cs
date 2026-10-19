using Eduscape.Domain.Rooms;

namespace Eduscape.Api.Play;

/// <summary>
/// An object as students see it. Fields that do not apply to the type are null. Correct answers, tolerances and
/// hints are never included: answers are checked on the server, and hints arrive once the team has earned them.
/// </summary>
public sealed record PlayObjectDto(
    Guid Id,
    RoomObjectType Type,
    double X,
    double Y,
    string GraphicKey,
    string? UnlockedGraphicKey,
    Guid? UnlockedById,
    string? Question,
    AnswerType? AnswerType,
    string[]? Options,
    string? Text,
    Guid? TargetSceneId);

public sealed record PlaySceneDto(Guid Id, string Name, string BackgroundImageKey, PlayObjectDto[] Objects);

/// <summary>The room's content for the play view, the same for every team.</summary>
public sealed record PlayRoomDto(Guid Id, string Title, Guid? StartSceneId, PlaySceneDto[] Scenes)
{
    public static PlayRoomDto From(EscapeRoom room) => new(
        room.Id,
        room.Title,
        room.StartScene?.Id,
        [.. room.Scenes.OrderBy(s => s.SortOrder).Select(scene => new PlaySceneDto(
            scene.Id,
            scene.Name,
            scene.BackgroundImageKey,
            [.. scene.Objects.OrderBy(o => o.Id).Select(ToDto)]))]);

    private static PlayObjectDto ToDto(RoomObject roomObject)
    {
        var dto = new PlayObjectDto(
            roomObject.Id,
            roomObject.Type,
            roomObject.Position.X,
            roomObject.Position.Y,
            roomObject.GraphicKey,
            roomObject.UnlockedGraphicKey,
            roomObject.UnlockedById,
            Question: null,
            AnswerType: null,
            Options: null,
            Text: null,
            TargetSceneId: null);

        return roomObject switch
        {
            Lock @lock => dto with
            {
                Question = @lock.Question,
                AnswerType = @lock.AnswerType,
                Options = @lock.AnswerType == Domain.Rooms.AnswerType.MultipleChoice ? @lock.Options : null,
            },
            Letter letter => dto with { Text = letter.Text },
            Door door => dto with { TargetSceneId = door.TargetSceneId },
            _ => dto,
        };
    }
}
