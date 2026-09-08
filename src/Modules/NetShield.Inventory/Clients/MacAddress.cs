using System.Globalization;

namespace NetShield.Inventory.Clients;

/// <summary>
/// Reading and normalising a hardware address, and the three facts its first octet carries.
/// </summary>
/// <remarks>
/// <para>
/// A MAC arrives spelled several ways. The collector renders an SNMP octet string as
/// colon-separated uppercase hex, an operator types <c>aa-bb-cc-dd-ee-ff</c>, and a Cisco device
/// prints <c>aabb.ccdd.eeff</c>. All three mean one address, and a client keyed by the string
/// would be three clients. Everything stored goes through <see cref="Normalize"/> first, so the
/// unique index on <c>clients.mac_address</c> is an index on the address rather than on how
/// somebody happened to write it.
/// </para>
/// <para>
/// <strong>The OUI is stored and its vendor name is not.</strong> The first three octets are the
/// IEEE Organizationally Unique Identifier, which is a fact about the address and costs nothing
/// to record. Turning it into "Apple, Inc." needs the IEEE registry — a file with a licence, a
/// size, and an update cadence, none of which should be decided by a package that merely wanted
/// a label. So the prefix is here, the name is not, and the package that decides how the
/// registry gets into this repository resolves one from the other.
/// </para>
/// </remarks>
internal static class MacAddress
{
    /// <summary>How many octets a MAC has. NetShield reads no EUI-64 address.</summary>
    internal const int Octets = 6;

    /// <summary>The normalised length: six pairs of hex and five separators.</summary>
    internal const int Length = (Octets * 2) + (Octets - 1);

    /// <summary>The length of the OUI half — three pairs of hex and two separators.</summary>
    internal const int OuiLength = 8;

    /// <summary>
    /// <paramref name="value"/> as colon-separated uppercase hex, or <see langword="null"/> when
    /// it is not six octets however it was spelled.
    /// </summary>
    /// <remarks>
    /// Every non-hexadecimal character is treated as a separator, which is what makes one parser
    /// cover colons, hyphens, Cisco's dotted quads and the bare twelve characters at once. What
    /// it deliberately does not do is accept a value with the wrong number of hex digits: an
    /// eleven-digit string is a corrupted address rather than one worth guessing at.
    /// </remarks>
    internal static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        Span<char> digits = stackalloc char[Octets * 2];
        int count = 0;

        foreach (char character in value)
        {
            if (!Uri.IsHexDigit(character))
            {
                continue;
            }

            if (count == digits.Length)
            {
                // More hex than an address has. Better to refuse than to keep the first twelve
                // characters of something that is not an address at all.
                return null;
            }

            digits[count++] = char.ToUpperInvariant(character);
        }

        if (count != digits.Length)
        {
            return null;
        }

        Span<char> formatted = stackalloc char[Length];

        for (int octet = 0; octet < Octets; octet++)
        {
            int at = octet * 3;

            if (octet > 0)
            {
                formatted[at - 1] = ':';
            }

            formatted[at] = digits[octet * 2];
            formatted[at + 1] = digits[(octet * 2) + 1];
        }

        return new string(formatted);
    }

    /// <summary>The first three octets of a normalised address.</summary>
    internal static string Oui(string normalized)
    {
        ArgumentNullException.ThrowIfNull(normalized);

        return normalized.Length >= OuiLength ? normalized[..OuiLength] : normalized;
    }

    /// <summary>
    /// Whether the address's local bit is set — bit 1 of the first octet, IEEE 802's
    /// U/L flag.
    /// </summary>
    /// <remarks>
    /// A locally administered address is one somebody assigned rather than one a manufacturer
    /// burned in, so its OUI stands for no vendor at all. That matters on a real network: MAC
    /// randomisation on a phone or a laptop produces exactly this, and reporting the client as
    /// "unknown manufacturer" would be reading a deliberate absence as a gap.
    /// </remarks>
    internal static bool IsLocallyAdministered(string normalized) => (FirstOctet(normalized) & 0x02) != 0;

    /// <summary>
    /// Whether the address's group bit is set — bit 0 of the first octet.
    /// </summary>
    /// <remarks>
    /// A multicast or broadcast address is not an endpoint. It is recorded rather than refused,
    /// because a forwarding database that reports one is telling the truth about itself, but it
    /// is a fact a reader should be able to see rather than one to be silently listed among the
    /// hosts.
    /// </remarks>
    internal static bool IsMulticast(string normalized) => (FirstOctet(normalized) & 0x01) != 0;

    private static byte FirstOctet(string normalized)
    {
        ArgumentNullException.ThrowIfNull(normalized);

        return normalized.Length >= 2
            && byte.TryParse(normalized[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte octet)
                ? octet
                : (byte)0;
    }
}
