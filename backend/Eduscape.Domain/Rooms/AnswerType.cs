namespace Eduscape.Domain.Rooms;

public enum AnswerType
{
    /// <summary>Exact code, e.g. "0427". Leading zeros matter, no numeric normalisation.</summary>
    Code,

    /// <summary>Free text; numeric answers are compared as numbers with an optional tolerance.</summary>
    Text,

    /// <summary>One of the lock's options.</summary>
    MultipleChoice,
}
