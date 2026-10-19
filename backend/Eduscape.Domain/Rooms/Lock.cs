using System.Diagnostics.CodeAnalysis;
using Eduscape.Domain.Common;

namespace Eduscape.Domain.Rooms;

/// <summary>
/// A question that must be answered correctly to unlock it. Answers are only ever checked on the server.
/// While a teacher is editing, the question, answer and options may be unfinished; <see cref="EscapeRoom.Validate"/>
/// reports what is missing, and a room with such problems cannot be played.
/// </summary>
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
        IEnumerable<string>? options = null,
        Guid? id = null)
        : base(position, graphicKey, unlockedGraphicKey, id)
    {
        SetQuestion(question, hint);
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

    [MemberNotNull(nameof(Question))]
    public void SetQuestion(string question, string? hint)
    {
        Question = Guard.Draft(question, TextLimits.Question);
        Hint = Guard.Optional(hint, TextLimits.Hint);
    }

    [MemberNotNull(nameof(CorrectAnswer))]
    public void SetAnswer(AnswerType answerType, string correctAnswer, decimal? tolerance = null, IEnumerable<string>? options = null)
    {
        var answer = Guard.Draft(correctAnswer, TextLimits.Answer);

        if (tolerance is not null && (answerType != AnswerType.Text || tolerance < 0))
        {
            throw new DomainException(DomainErrors.LockToleranceInvalid);
        }

        var choices = (options ?? []).Select(o => Guard.Draft(o, TextLimits.Answer)).ToArray();
        if (answerType != AnswerType.MultipleChoice && choices.Length > 0)
        {
            throw new DomainException(DomainErrors.LockOptionsInvalid);
        }

        AnswerType = answerType;
        CorrectAnswer = answer;
        Tolerance = tolerance;
        Options = choices;
    }

    internal override IEnumerable<string> ContentErrors()
    {
        if (Question.Length == 0)
        {
            yield return DomainErrors.LockQuestionMissing;
        }

        if (CorrectAnswer.Length == 0)
        {
            yield return DomainErrors.LockAnswerMissing;
        }

        if (AnswerType != AnswerType.MultipleChoice)
        {
            yield break;
        }

        var distinct = Options.Distinct(StringComparer.OrdinalIgnoreCase).Count();
        if (Options.Length < 2 || distinct != Options.Length || Options.Any(o => o.Length == 0))
        {
            yield return DomainErrors.LockOptionsInvalid;
        }
        else if (CorrectAnswer.Length > 0 && !Options.Contains(CorrectAnswer, StringComparer.OrdinalIgnoreCase))
        {
            yield return DomainErrors.LockAnswerNotInOptions;
        }
    }
}
