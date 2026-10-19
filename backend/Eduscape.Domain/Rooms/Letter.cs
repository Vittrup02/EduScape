using System.Diagnostics.CodeAnalysis;
using Eduscape.Domain.Common;

namespace Eduscape.Domain.Rooms;

/// <summary>A clue written by the teacher, e.g. "Læs side 42 i bogen".</summary>
public sealed class Letter : RoomObject
{
    public Letter(Position position, string graphicKey, string text, Guid? id = null)
        : base(position, graphicKey, unlockedGraphicKey: null, id)
    {
        SetText(text);
    }

    // For EF Core
    private Letter() => Text = null!;

    public override RoomObjectType Type => RoomObjectType.Letter;

    /// <summary>May be empty while the teacher is editing; reported by <see cref="EscapeRoom.Validate"/>.</summary>
    public string Text { get; private set; }

    [MemberNotNull(nameof(Text))]
    public void SetText(string text) => Text = Guard.Draft(text, TextLimits.LetterText);

    internal override IEnumerable<string> ContentErrors() =>
        Text.Length == 0 ? [DomainErrors.LetterTextMissing] : [];
}
