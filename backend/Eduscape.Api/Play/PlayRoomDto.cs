using System.Security.Cryptography;
using Eduscape.Api.Uploads;
using Eduscape.Domain.Rooms;

namespace Eduscape.Api.Play;

/// <summary>An item to put in order, with a short-lived link to its image (null without one).</summary>
public sealed record PlayOrderItemDto(Guid Id, string Text, string? ImageUrl);

/// <summary>
/// One step of a lock as students see it: the question, never the answer. Options for MultipleChoice, items for Order
/// (shuffled, never in the correct order); null otherwise.
/// </summary>
public sealed record PlayStepDto(string Question, AnswerType AnswerType, string[]? Options, PlayOrderItemDto[]? OrderItems);

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
    PlayStepDto[]? Steps,
    string? Text,
    Guid? TargetSceneId);

/// <summary>
/// <c>BackgroundImageUrl</c> is a short-lived link to an uploaded background; null for built-in ones.
/// <c>Narration</c> is the teacher's story text, shown when the student enters the scene; null when there is none.
/// </summary>
public sealed record PlaySceneDto(
    Guid Id,
    string Name,
    string BackgroundImageKey,
    string? BackgroundImageUrl,
    PlayObjectDto[] Objects,
    string? Narration);

/// <summary>
/// The room's content for the play view. It is the same for every team, except that each team gets the items of its
/// order steps shuffled its own way (the same way on every device of the team, so teammates see the same puzzle).
/// </summary>
public sealed record PlayRoomDto(Guid Id, string Title, Guid? StartSceneId, int WrongAnswerCooldownSeconds, PlaySceneDto[] Scenes)
{
    public static PlayRoomDto From(EscapeRoom room, ImageUrls imageUrls, Guid teamId) => new(
        room.Id,
        room.Title,
        room.StartScene?.Id,
        room.WrongAnswerCooldownSeconds,
        [.. room.Scenes.OrderBy(s => s.SortOrder).Select(scene => new PlaySceneDto(
            scene.Id,
            scene.Name,
            scene.BackgroundImageKey,
            imageUrls.For(scene.BackgroundImageKey),
            [.. scene.Objects.OrderBy(o => o.Id).Select(o => ToDto(o, imageUrls, teamId))],
            scene.Narration))]);

    /// <summary>
    /// The step's items in an order that depends on the team and the step but is never the correct one, so students
    /// always have something to sort. The ids are random, so sorting by them reveals nothing either.
    /// </summary>
    public static IReadOnlyList<OrderItem> Shuffle(LockStep step, Guid teamId)
    {
        var items = step.OrderItems.ToArray();
        if (items.Length < 2)
        {
            return items;
        }

        var seed = SHA256.HashData([.. teamId.ToByteArray(), .. step.Id.ToByteArray()]);
        new Random(BitConverter.ToInt32(seed)).Shuffle(items);
        if (items.Select(i => i.Id).SequenceEqual(step.OrderItems.Select(i => i.Id)))
        {
            // Shuffled back into place: move the first item to the end.
            items = [.. items.Skip(1), items[0]];
        }

        return items;
    }

    private static PlayObjectDto ToDto(RoomObject roomObject, ImageUrls imageUrls, Guid teamId)
    {
        var dto = new PlayObjectDto(
            roomObject.Id,
            roomObject.Type,
            roomObject.Position.X,
            roomObject.Position.Y,
            roomObject.GraphicKey,
            roomObject.UnlockedGraphicKey,
            roomObject.UnlockedById,
            Steps: null,
            Text: null,
            TargetSceneId: null);

        return roomObject switch
        {
            Lock @lock => dto with { Steps = [.. @lock.Steps.Select(step => ToDto(step, imageUrls, teamId))] },
            Letter letter => dto with { Text = letter.Text },
            TextBox textBox => dto with { Text = textBox.Text },
            Door door => dto with { TargetSceneId = door.TargetSceneId },
            _ => dto,
        };
    }

    private static PlayStepDto ToDto(LockStep step, ImageUrls imageUrls, Guid teamId) => new(
        step.Question,
        step.AnswerType,
        step.AnswerType == AnswerType.MultipleChoice ? [.. step.Options] : null,
        step.AnswerType == AnswerType.Order
            ? [.. Shuffle(step, teamId).Select(item => new PlayOrderItemDto(
                item.Id, item.Text, item.ImageKey is null ? null : imageUrls.For(item.ImageKey)))]
            : null);
}
