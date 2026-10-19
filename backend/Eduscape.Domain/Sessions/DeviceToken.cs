using System.Security.Cryptography;
using System.Text;

namespace Eduscape.Domain.Sessions;

/// <summary>
/// The random token a student's browser keeps to reconnect to its team. Only its hash is stored, so the database
/// never holds a token that could be used to impersonate a student.
/// </summary>
public static class DeviceToken
{
    private const int TokenBytes = 32;

    /// <summary>A new token: 256 random bits, base64url-encoded (43 characters).</summary>
    public static string Create() => Base64Url(RandomNumberGenerator.GetBytes(TokenBytes));

    /// <summary>SHA-256 of the token as lowercase hex (64 characters).</summary>
    public static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
