using System.Text.Json;
using System.Text.Json.Serialization;
using Eduscape.Domain.Rooms;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Eduscape.Infrastructure.Persistence.Configurations;

/// <summary>
/// Stores a lock's steps as one jsonb value. The editor always replaces a lock's steps as a whole and nothing queries
/// inside them, so a document fits better than tables of steps and items. The JSON shape is defined here, not by the
/// domain classes; the migration that moved the old lock columns into steps writes the same shape.
/// </summary>
internal static class LockStepsJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static readonly ValueConverter<List<LockStep>, string> Converter = new(
        steps => Serialize(steps),
        json => Deserialize(json));

    /// <summary>Steps are immutable, so two lists are equal when they serialize the same.</summary>
    public static readonly ValueComparer<List<LockStep>> Comparer = new(
        (a, b) => Serialize(a!) == Serialize(b!),
        steps => Serialize(steps).GetHashCode(StringComparison.Ordinal),
        steps => steps.ToList());

    public static string Serialize(List<LockStep> steps) =>
        JsonSerializer.Serialize(steps.Select(ToJson).ToList(), Options);

    public static List<LockStep> Deserialize(string json) =>
        [.. (JsonSerializer.Deserialize<List<StepJson>>(json, Options) ?? []).Select(FromJson)];

    private static StepJson ToJson(LockStep step) => new(
        step.Id,
        step.Question,
        step.AnswerType,
        step.CorrectAnswer,
        step.Tolerance,
        step.Hint,
        [.. step.Options],
        [.. step.AcceptedAnswers],
        step.AcceptTypos,
        [.. step.OrderItems.Select(i => new OrderItemJson(i.Id, i.Text, i.ImageKey))]);

    private static LockStep FromJson(StepJson json) => new(
        json.Question,
        json.AnswerType,
        json.CorrectAnswer,
        json.Tolerance,
        json.Hint,
        json.Options,
        json.AcceptedAnswers,
        json.AcceptTypos,
        (json.OrderItems ?? []).Select(i => new OrderItem(i.Id, i.Text, i.ImageKey)),
        json.Id);

    private sealed record StepJson(
        Guid Id,
        string Question,
        AnswerType AnswerType,
        string CorrectAnswer,
        decimal? Tolerance,
        string? Hint,
        string[]? Options,
        string[]? AcceptedAnswers,
        bool AcceptTypos,
        OrderItemJson[]? OrderItems);

    private sealed record OrderItemJson(Guid Id, string Text, string? ImageKey);
}
