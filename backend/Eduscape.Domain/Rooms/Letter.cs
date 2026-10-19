using Eduscape.Domain.Common;

namespace Eduscape.Domain.Rooms;

/// <summary>A clue written by the teacher, e.g. "Læs side 42 i bogen".</summary>
public sealed class Letter : RoomObject
{
    public Letter(Position position, string graphicKey, string text)
        : base(position, graphicKey, unlockedGraphicKey: null)
    {
        Text = Guard.Required(text, TextLimits.LetterText);
    }

    // For EF Core
    private Letter() => Text = null!;

    public override RoomObjectType Type => RoomObjectType.Letter;

    public string Text { get; private set; }

    public void SetText(string text) => Text = Guard.Required(text, TextLimits.LetterText);
}
