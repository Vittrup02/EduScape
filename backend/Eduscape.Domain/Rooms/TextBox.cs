using System.Diagnostics.CodeAnalysis;
using Eduscape.Domain.Common;

namespace Eduscape.Domain.Rooms;

/// <summary>
/// A piece of the teacher's story written straight onto the scene, e.g. "Vagten er gået til frokost". Unlike a letter
/// it is read where it stands, without opening anything, and it completes nothing.
/// </summary>
public sealed class TextBox : RoomObject
{
    /// <summary>Text boxes have no graphic of their own; the key marks the built-in look.</summary>
    public const string GraphicKeyValue = "builtin:text-box";

    public TextBox(Position position, string text, Guid? id = null)
        : base(position, GraphicKeyValue, unlockedGraphicKey: null, id)
    {
        SetText(text);
    }

    // For EF Core
    private TextBox() => Text = null!;

    public override RoomObjectType Type => RoomObjectType.TextBox;

    /// <summary>May be empty while the teacher is editing; reported by <see cref="EscapeRoom.Validate"/>.</summary>
    public string Text { get; private set; }

    [MemberNotNull(nameof(Text))]
    public void SetText(string text) => Text = Guard.Draft(text, TextLimits.TextBox);

    internal override IEnumerable<string> ContentErrors() =>
        Text.Length == 0 ? [DomainErrors.TextBoxTextMissing] : [];
}
