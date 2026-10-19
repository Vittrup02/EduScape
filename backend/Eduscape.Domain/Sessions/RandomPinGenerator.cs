using System.Globalization;
using System.Security.Cryptography;

namespace Eduscape.Domain.Sessions;

/// <summary>Cryptographically random six-digit PINs (000000–999999), so PINs cannot be predicted.</summary>
public sealed class RandomPinGenerator : IPinGenerator
{
    private static readonly int PinSpace = (int)Math.Pow(10, Session.PinLength);

    public string Next() =>
        RandomNumberGenerator.GetInt32(PinSpace).ToString($"D{Session.PinLength}", CultureInfo.InvariantCulture);
}
