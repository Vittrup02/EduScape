using Eduscape.Domain.Common;

namespace Eduscape.Domain.Rooms;

/// <summary>
/// One question of a lock. A lock has one or more steps that students answer in order; the lock opens after the last
/// one. Each step has its own answer type, answer, tolerance and hint. Answers are only ever checked on the server.
/// While a teacher is editing, a step may be unfinished; <see cref="EscapeRoom.Validate"/> reports what is missing.
/// Steps are immutable: the editor replaces a lock's steps as a whole.
/// </summary>
public sealed class LockStep
{
    public const int MaxOptions = 10;
    public const int MaxAcceptedAnswers = 10;
    public const int MaxOrderItems = 12;

    /// <param name="id">Set by the editor, so that undo brings a step back as the same step.</param>
    public LockStep(
        string question,
        AnswerType answerType,
        string correctAnswer,
        decimal? tolerance = null,
        string? hint = null,
        IEnumerable<string>? options = null,
        IEnumerable<string>? acceptedAnswers = null,
        bool acceptTypos = false,
        IEnumerable<OrderItem>? orderItems = null,
        Guid? id = null)
    {
        if (!Enum.IsDefined(answerType))
        {
            throw new DomainException(DomainErrors.LockStepInvalid);
        }

        var choices = Texts(options, MaxOptions);
        var accepted = Texts(acceptedAnswers, MaxAcceptedAnswers).Where(a => a.Length > 0).ToArray();
        var items = (orderItems ?? []).ToArray();
        if (items.Length > MaxOrderItems)
        {
            throw new DomainException(DomainErrors.ListTooLong);
        }

        // Settings only apply to the answer types they belong to; anything else is a broken request.
        var isText = answerType == AnswerType.Text;
        if (tolerance is not null && (!isText || tolerance < 0))
        {
            throw new DomainException(DomainErrors.LockToleranceInvalid);
        }

        if (choices.Length > 0 && answerType != AnswerType.MultipleChoice)
        {
            throw new DomainException(DomainErrors.LockOptionsInvalid);
        }

        if (((accepted.Length > 0 || acceptTypos) && !isText)
            || (items.Length > 0 && answerType != AnswerType.Order)
            || items.Select(i => i.Id).Distinct().Count() != items.Length)
        {
            throw new DomainException(DomainErrors.LockStepInvalid);
        }

        Id = id ?? Guid.CreateVersion7();
        Question = Guard.Draft(question, TextLimits.Question);
        AnswerType = answerType;
        CorrectAnswer = answerType == AnswerType.Order ? string.Empty : Guard.Draft(correctAnswer, TextLimits.Answer);
        Tolerance = tolerance;
        Hint = Guard.Optional(hint, TextLimits.Hint);
        Options = choices;
        AcceptedAnswers = accepted;
        AcceptTypos = acceptTypos;
        OrderItems = items;
    }

    public Guid Id { get; }

    public string Question { get; }

    public AnswerType AnswerType { get; }

    /// <summary>The answer for Code, Text and MultipleChoice; empty for <see cref="AnswerType.Order"/>.</summary>
    public string CorrectAnswer { get; }

    /// <summary>Accepted numeric deviation for <see cref="AnswerType.Text"/> answers that are numbers, e.g. 1.0.</summary>
    public decimal? Tolerance { get; }

    /// <summary>Shown automatically after <see cref="Sessions.Team.WrongAttemptsBeforeHint"/> wrong attempts on this step.</summary>
    public string? Hint { get; }

    /// <summary>Choices for <see cref="AnswerType.MultipleChoice"/>; empty for other answer types.</summary>
    public IReadOnlyList<string> Options { get; }

    /// <summary>Other answers that are also right, e.g. synonyms. Only for <see cref="AnswerType.Text"/>.</summary>
    public IReadOnlyList<string> AcceptedAnswers { get; }

    /// <summary>Accept small spelling mistakes (see <see cref="AnswerMatcher"/>). Only for <see cref="AnswerType.Text"/>.</summary>
    public bool AcceptTypos { get; }

    /// <summary>The items of an <see cref="AnswerType.Order"/> step, in the correct order. Never sent to students in it.</summary>
    public IReadOnlyList<OrderItem> OrderItems { get; }

    /// <summary>What a teacher still has to fill in before the step can be played.</summary>
    internal IEnumerable<string> ContentErrors()
    {
        if (Question.Length == 0)
        {
            yield return DomainErrors.LockQuestionMissing;
        }

        switch (AnswerType)
        {
            case AnswerType.Order:
                if (OrderItems.Count < 2 || OrderItems.Any(i => i.Text.Length == 0))
                {
                    yield return DomainErrors.OrderItemsInvalid;
                }

                break;

            case AnswerType.MultipleChoice:
                if (CorrectAnswer.Length == 0)
                {
                    yield return DomainErrors.LockAnswerMissing;
                }

                var distinct = Options.Distinct(StringComparer.OrdinalIgnoreCase).Count();
                if (Options.Count < 2 || distinct != Options.Count || Options.Any(o => o.Length == 0))
                {
                    yield return DomainErrors.LockOptionsInvalid;
                }
                else if (CorrectAnswer.Length > 0 && !Options.Contains(CorrectAnswer, StringComparer.OrdinalIgnoreCase))
                {
                    yield return DomainErrors.LockAnswerNotInOptions;
                }

                break;

            default:
                if (CorrectAnswer.Length == 0)
                {
                    yield return DomainErrors.LockAnswerMissing;
                }

                break;
        }
    }

    private static string[] Texts(IEnumerable<string>? values, int max)
    {
        var texts = (values ?? []).Select(v => Guard.Draft(v, TextLimits.Answer)).ToArray();
        return texts.Length > max ? throw new DomainException(DomainErrors.ListTooLong) : texts;
    }
}

/// <summary>
/// One thing students put in order, e.g. a step in a reaction. The id is random (not ordered by time), so the ids
/// students get never reveal the correct order. The optional image is an upload the server has processed.
/// </summary>
public sealed class OrderItem
{
    public OrderItem(Guid id, string text, string? imageKey = null)
    {
        if (id == Guid.Empty || (imageKey is not null && !ImageKeys.IsUpload(imageKey)))
        {
            throw new DomainException(imageKey is null ? DomainErrors.LockStepInvalid : DomainErrors.ImageKeyInvalid);
        }

        Id = id;
        Text = Guard.Draft(text, TextLimits.OrderItem);
        ImageKey = imageKey;
    }

    public Guid Id { get; }

    public string Text { get; }

    /// <summary>An uploaded image (never a free URL), shown with the text; null when there is none.</summary>
    public string? ImageKey { get; }
}
