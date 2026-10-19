using System.Diagnostics.CodeAnalysis;
using Eduscape.Domain.Common;

namespace Eduscape.Domain.Rooms;

/// <summary>A question that must be answered correctly to unlock it. Answers are only ever checked on the server.</summary>
public sealed class Lock : RoomObject
{
    public Lock(
        Position position,
        string graphicKey,
        string? unlockedGraphicKey,
        string question,
        AnswerType answerType,
        string correctAnswer,
        decimal? tolerance = null,
        string? hint = null,
        IEnumerable<string>? options = null)
        : base(position, graphicKey, unlockedGraphicKey)
    {
        Question = Guard.Required(question, TextLimits.Question);
        Hint = Guard.Optional(hint, TextLimits.Hint);
        SetAnswer(answerType, correctAnswer, tolerance, options);
    }

    // For EF Core
    private Lock()
    {
        Question = null!;
        CorrectAnswer = null!;
    }

    public override RoomObjectType Type => RoomObjectType.Lock;

    public string Question { get; private set; }

    public AnswerType AnswerType { get; private set; }

    public string CorrectAnswer { get; private set; }

    /// <summary>Accepted numeric deviation for <see cref="AnswerType.Text"/> answers, e.g. 0.01.</summary>
    public decimal? Tolerance { get; private set; }

    /// <summary>Shown automatically after <see cref="Sessions.Team.WrongAttemptsBeforeHint"/> wrong attempts by the team.</summary>
    public string? Hint { get; private set; }

    /// <summary>Choices for <see cref="AnswerType.MultipleChoice"/>; empty for other answer types.</summary>
    public string[] Options { get; private set; } = [];

    public void SetQuestion(string question, string? hint)
    {
        Question = Guard.Required(question, TextLimits.Question);
        Hint = Guard.Optional(hint, TextLimits.Hint);
    }

    [MemberNotNull(nameof(CorrectAnswer))]
    public void SetAnswer(AnswerType answerType, string correctAnswer, decimal? tolerance = null, IEnumerable<string>? options = null)
    {
        var answer = Guard.Required(correctAnswer, TextLimits.Answer);

        if (tolerance is not null && (answerType != AnswerType.Text || tolerance < 0))
        {
            throw new DomainException(DomainErrors.LockToleranceInvalid);
        }

        var choices = (options ?? []).Select(o => Guard.Required(o, TextLimits.Answer)).ToArray();
        if (answerType == AnswerType.MultipleChoice)
        {
            var distinct = choices.Distinct(StringComparer.OrdinalIgnoreCase).Count();
            if (choices.Length < 2 || distinct != choices.Length)
            {
                throw new DomainException(DomainErrors.LockOptionsInvalid);
            }

            if (!choices.Contains(answer, StringComparer.OrdinalIgnoreCase))
            {
                throw new DomainException(DomainErrors.LockAnswerNotInOptions);
            }
        }
        else if (choices.Length > 0)
        {
            throw new DomainException(DomainErrors.LockOptionsInvalid);
        }

        AnswerType = answerType;
        CorrectAnswer = answer;
        Tolerance = tolerance;
        Options = choices;
    }
}
