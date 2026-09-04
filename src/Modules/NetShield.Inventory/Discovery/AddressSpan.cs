using System.Net;
using System.Net.Sockets;

namespace NetShield.Inventory.Discovery;

/// <summary>
/// A contiguous run of addresses: what one sweep job probes.
/// </summary>
/// <remarks>
/// <para>
/// A span rather than a CIDR block, because a job's slice of a seed is not generally a prefix.
/// <see cref="AddressRange"/> decides which addresses are worth probing — skipping the network
/// and broadcast addresses of an IPv4 block — and then cuts what is left into pieces small
/// enough to be one job. Those pieces have no prefix of their own, and pretending they did is
/// how the API's count and the collector's work drift apart.
/// </para>
/// <para>
/// It travels in a job's parameters as its first and last address, which is two strings whatever
/// the span holds — a list of addresses would put a job's whole slice on the wire and into the
/// <c>parameters</c> column.
/// </para>
/// </remarks>
/// <param name="Family">The address family both ends belong to.</param>
/// <param name="First">The first address, as a number.</param>
/// <param name="Last">The last address, as a number. Inclusive.</param>
internal readonly record struct AddressSpan(AddressFamily Family, UInt128 First, UInt128 Last)
{
    /// <summary>The first address.</summary>
    internal IPAddress FirstAddress => AddressRange.ToAddress(First, Family);

    /// <summary>The last address, which is in the span.</summary>
    internal IPAddress LastAddress => AddressRange.ToAddress(Last, Family);

    /// <summary>
    /// How many addresses are in the span, saturating at <see cref="long.MaxValue"/> rather than
    /// wrapping.
    /// </summary>
    internal long Count
    {
        get
        {
            UInt128 count = Last - First + UInt128.One;

            return count > (UInt128)long.MaxValue ? long.MaxValue : (long)count;
        }
    }

    /// <summary>Whether the span holds <paramref name="address"/>.</summary>
    internal bool Contains(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.AddressFamily != Family)
        {
            return false;
        }

        UInt128 number = AddressRange.ToNumber(address);

        return number >= First && number <= Last;
    }

    /// <summary>The part of this span that <paramref name="other"/> also covers, if any.</summary>
    internal AddressSpan? Intersect(AddressSpan other)
    {
        if (other.Family != Family)
        {
            return null;
        }

        UInt128 first = UInt128.Max(First, other.First);
        UInt128 last = UInt128.Min(Last, other.Last);

        return first > last ? null : new AddressSpan(Family, first, last);
    }
}
