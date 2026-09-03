using System.Globalization;

namespace NetShield.Identity.Passwords;

/// <summary>
/// One stored Argon2id hash, in PHC string format:
/// <c>$argon2id$v=19$m=19456,t=2,p=1$&lt;salt&gt;$&lt;hash&gt;</c>.
/// </summary>
/// <remarks>
/// The parameters travel with the hash on purpose. A column holding only the digest can never be
/// re-costed without invalidating every password in it, whereas this format lets the verifier
/// reproduce the hash exactly as it was made and report that it should be remade stronger.
/// Salt and digest are unpadded base64, as the format specifies.
/// </remarks>
internal sealed record PasswordHash(int MemoryKib, int Iterations, int Parallelism, byte[] Salt, byte[] Digest)
{
    /// <summary>The only algorithm NetShield stores. A hash naming anything else is rejected.</summary>
    internal const string Algorithm = "argon2id";

    /// <summary>The Argon2 version these hashes are made with, as the format spells it.</summary>
    internal const int Version = 19;

    /// <summary>Renders the hash for storage.</summary>
    public string Format() => string.Create(
        CultureInfo.InvariantCulture,
        $"${Algorithm}$v={Version}$m={MemoryKib},t={Iterations},p={Parallelism}${Encode(Salt)}${Encode(Digest)}");

    /// <summary>
    /// Reads a stored hash. Returns <see langword="false"/> for anything malformed rather than
    /// throwing: a corrupt row is a failed sign-in, not a 500.
    /// </summary>
    public static bool TryParse(string? encoded, out PasswordHash? hash)
    {
        hash = null;

        if (string.IsNullOrEmpty(encoded))
        {
            return false;
        }

        // A leading '$' produces an empty first field, which is part of the format.
        string[] fields = encoded.Split('$');

        if (fields.Length != 6
            || fields[0].Length != 0
            || !string.Equals(fields[1], Algorithm, StringComparison.Ordinal)
            || !TryParseVersion(fields[2])
            || !TryParseCosts(fields[3], out int memoryKib, out int iterations, out int parallelism)
            || !TryDecode(fields[4], out byte[]? salt)
            || !TryDecode(fields[5], out byte[]? digest))
        {
            return false;
        }

        hash = new PasswordHash(memoryKib, iterations, parallelism, salt, digest);
        return true;
    }

    private static bool TryParseVersion(string field) =>
        field.StartsWith("v=", StringComparison.Ordinal)
        && int.TryParse(field.AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture, out int version)
        && version == Version;

    private static bool TryParseCosts(string field, out int memoryKib, out int iterations, out int parallelism)
    {
        memoryKib = 0;
        iterations = 0;
        parallelism = 0;

        string[] parts = field.Split(',');

        return parts.Length == 3
            && TryParseCost(parts[0], "m=", out memoryKib)
            && TryParseCost(parts[1], "t=", out iterations)
            && TryParseCost(parts[2], "p=", out parallelism);
    }

    private static bool TryParseCost(string part, string prefix, out int value)
    {
        value = 0;

        return part.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(part.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out value)
            && value > 0;
    }

    private static string Encode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=');

    private static bool TryDecode(string value, out byte[] decoded)
    {
        decoded = [];

        if (value.Length == 0)
        {
            return false;
        }

        string padded = value.PadRight(value.Length + ((4 - (value.Length % 4)) % 4), '=');

        try
        {
            decoded = Convert.FromBase64String(padded);
        }
        catch (FormatException)
        {
            return false;
        }

        return decoded.Length > 0;
    }
}
