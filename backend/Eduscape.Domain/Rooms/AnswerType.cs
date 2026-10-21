namespace Eduscape.Domain.Rooms;

public enum AnswerType
{
    /// <summary>Exact code, e.g. "0427". Leading zeros matter, no numeric normalisation, no spelling mistakes.</summary>
    Code,

    /// <summary>
    /// Free text; numeric answers are compared as numbers with an optional tolerance. Synonyms and small spelling
    /// mistakes can be accepted.
    /// </summary>
    Text,

    /// <summary>One of the step's options.</summary>
    MultipleChoice,

    /// <summary>Students drag the step's items into the right order.</summary>
    Order,
}
