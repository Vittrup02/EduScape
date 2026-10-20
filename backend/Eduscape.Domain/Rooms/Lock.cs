using Eduscape.Domain.Common;

namespace Eduscape.Domain.Rooms;

/// <summary>
/// A lock has two separate parts: its graphic (a padlock, a chest, a safe …) and its puzzle, which is one or more
/// <see cref="LockStep"/>s answered in order. The lock opens after the last step; a lock with one step is an ordinary
/// question. The graphic can change without touching the puzzle. Answers are only ever checked on the server.
/// </summary>
public sealed class Lock : RoomObject
{
    public const int MaxSteps = 10;

    private List<LockStep> _steps = [];

    public Lock(Position position, string graphicKey, string? unlockedGraphicKey, IEnumerable<LockStep> steps, Guid? id = null)
        : base(position, graphicKey, unlockedGraphicKey, id)
    {
        SetSteps(steps);
    }

    /// <summary>A lock with a single question, the common case.</summary>
    public Lock(
        Position position,
        string graphicKey,
        string? unlockedGraphicKey,
        string question,
        AnswerType answerType,
        string correctAnswer,
        decimal? tolerance = null,
        string? hint = null,
        IEnumerable<string>? options = null,
        Guid? id = null)
        : this(
            position,
            graphicKey,
            unlockedGraphicKey,
            [new LockStep(question, answerType, correctAnswer, tolerance, hint, options)],
            id)
    {
    }

    // For EF Core
    private Lock()
    {
    }

    public override RoomObjectType Type => RoomObjectType.Lock;

    /// <summary>The puzzle, in the order students answer it. Empty only while the teacher hasn't chosen a puzzle yet.</summary>
    public IReadOnlyList<LockStep> Steps => _steps;

    public void SetSteps(IEnumerable<LockStep> steps)
    {
        var list = steps.ToList();
        if (list.Count > MaxSteps)
        {
            throw new DomainException(DomainErrors.ListTooLong);
        }

        if (list.Select(s => s.Id).Distinct().Count() != list.Count)
        {
            throw new DomainException(DomainErrors.LockStepInvalid);
        }

        _steps = list;
    }

    /// <summary>The steps' own problems, with the step they belong to; a lock without a puzzle has no step.</summary>
    internal IEnumerable<(string Code, int? StepIndex)> PuzzleErrors()
    {
        if (_steps.Count == 0)
        {
            yield return (DomainErrors.LockPuzzleMissing, null);
            yield break;
        }

        for (var i = 0; i < _steps.Count; i++)
        {
            foreach (var code in _steps[i].ContentErrors())
            {
                yield return (code, i);
            }
        }
    }
}
