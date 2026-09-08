using System.Net;

namespace NetShield.Inventory.Resolution;

/// <summary>
/// How a cached address history is named in Redis.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Keyed by address, which is what makes invalidation possible at all.</strong> The
/// alternative — keying by the pair of address and instant — has an unbounded key space and
/// nothing to invalidate: a new binding for an address would have to expire every timestamp
/// anybody had ever asked about. Keying by address means one entry holds that address's recent
/// history, one write invalidates it, and the resolver does the instant arithmetic in memory.
/// </para>
/// <para>
/// WP-1.1 anticipated exactly this. <c>DeviceUpdated</c> carries the device's previous address
/// as well as its current one, with the note that "the address resolution in WP-1.8 is the first
/// thing that will need it" — because a device moving from one address to another invalidates
/// two keys, and a subscriber that held only the new address could not name the old one.
/// </para>
/// <para>
/// The address is normalised through <see cref="IPAddress"/> before it is written into the key,
/// so <c>10.0.0.1</c> and <c>::ffff:10.0.0.1</c> cannot become two entries describing one host.
/// </para>
/// </remarks>
internal static class AssetCacheKeys
{
    /// <summary>
    /// The prefix every key carries, so a Redis holding more than this can be read by a person.
    /// </summary>
    internal const string Prefix = "netshield:asset:";

    /// <summary>The key holding one address's history.</summary>
    internal static string For(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        return Prefix + address;
    }

    /// <summary>
    /// The key for an address written as text, or nothing when the text is not an address.
    /// </summary>
    /// <remarks>
    /// The form the event subscribers need: a <c>DeviceCreated</c> carries its address as the
    /// string the contract declares, and an address that will not parse is one no key was ever
    /// written under.
    /// </remarks>
    internal static string? For(string? address) =>
        IPAddress.TryParse(address, out IPAddress? parsed) ? For(parsed) : null;
}
