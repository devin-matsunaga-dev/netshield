using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace NetShield.Identity.Authentication;

/// <summary>
/// Mints refresh tokens and reduces them to the digest that is stored.
/// </summary>
/// <remarks>
/// 256 bits from the cryptographic generator, base64url so it survives a cookie unescaped.
/// What goes in the database is the SHA-256 of that, unsalted and uniterated — correct here and
/// wrong for a password, because the input is full-entropy random rather than something a person
/// chose, so there is no candidate list to run against it.
/// </remarks>
public static class RefreshTokenGenerator
{
    /// <summary>Entropy per token, in bytes.</summary>
    public const int TokenBytes = 32;

    /// <summary>A fresh token. Held in memory and in one cookie, and stored nowhere.</summary>
    public static string Create() =>
        Base64UrlEncode(RandomNumberGenerator.GetBytes(TokenBytes));

    /// <summary>The lowercase hex digest a token is looked up by.</summary>
    public static string Hash(string token)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
