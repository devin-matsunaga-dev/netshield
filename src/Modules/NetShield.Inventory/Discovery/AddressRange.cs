using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

using NetShield.Platform.Results;

namespace NetShield.Inventory.Discovery;

/// <summary>
/// A CIDR block, and the addresses inside it that a sweep will actually probe.
/// </summary>
/// <remarks>
/// <para>
/// Hand-written rather than <see cref="IPNetwork"/> because two of the three things this package
/// needs are not on that type: splitting a block into sub-blocks small enough to be one job, and
/// counting the hosts in one without enumerating them. It also refuses a value whose host bits
/// are set, where an operator typing <c>10.0.0.5/24</c> means <c>10.0.0.0/24</c> and should not
/// have to be told so.
/// </para>
/// <para>
/// <strong>The network and broadcast addresses are not probed</strong> on an IPv4 block of /30 or
/// wider. Pinging the broadcast address of a subnet asks every host on it to answer at once,
/// which is a lot of traffic in exchange for an answer that names no host — so a /24 is 254
/// addresses here, which is also what the WP-1.6 criterion means by "a run over a /24". A /31 and
/// a /32 have no such addresses to skip, and IPv6 has no broadcast at all.
/// </para>
/// <para>
/// Arithmetic is done in <see cref="UInt128"/>, which holds an IPv6 address exactly and an IPv4
/// address with room to spare, so one implementation covers both families without a
/// <see cref="System.Numerics.BigInteger"/> allocation per address.
/// </para>
/// </remarks>
internal readonly record struct AddressRange
{
    private AddressRange(AddressFamily family, UInt128 network, int prefixLength)
    {
        Family = family;
        Network = network;
        PrefixLength = prefixLength;
    }

    /// <summary>Which address family this block belongs to.</summary>
    internal AddressFamily Family { get; }

    /// <summary>The network address, as a number.</summary>
    internal UInt128 Network { get; }

    /// <summary>How many leading bits are fixed.</summary>
    internal int PrefixLength { get; }

    /// <summary>How many bits an address of this family has.</summary>
    internal int AddressBits => Family == AddressFamily.InterNetwork ? 32 : 128;

    /// <summary>The last address in the block, as a number.</summary>
    internal UInt128 Last => Network | HostMask;

    /// <summary>The first address a sweep would probe, as a number.</summary>
    internal UInt128 FirstHost =>
        SkipsEdges ? Network + UInt128.One : Network;

    /// <summary>The last address a sweep would probe, as a number.</summary>
    internal UInt128 LastHost =>
        SkipsEdges ? Last - UInt128.One : Last;

    /// <summary>
    /// How many addresses a sweep of this block would probe, saturating at
    /// <see cref="long.MaxValue"/> rather than wrapping.
    /// </summary>
    /// <remarks>
    /// The span is measured before one is added to it, because <c>::/0</c> holds every address
    /// there is and the count of it does not fit in a <see cref="UInt128"/> at all.
    /// </remarks>
    internal long HostCount
    {
        get
        {
            UInt128 span = LastHost - FirstHost;

            return span >= (UInt128)long.MaxValue ? long.MaxValue : (long)span + 1;
        }
    }

    /// <summary>The network address itself.</summary>
    internal IPAddress BaseAddress => ToAddress(Network, Family);

    /// <summary>The bits an address inside this block is free to set.</summary>
    private UInt128 HostMask => Mask(AddressBits - PrefixLength);

    /// <summary>Whether the block is big enough to have a network and a broadcast address.</summary>
    private bool SkipsEdges =>
        Family == AddressFamily.InterNetwork && PrefixLength <= 30;

    /// <summary>
    /// Reads a CIDR block, or a bare address meaning a single host.
    /// </summary>
    /// <remarks>
    /// The block is normalised: host bits are cleared, so <c>10.0.0.5/24</c> and
    /// <c>10.0.0.0/24</c> are the same value and cannot both be stored as if they were different
    /// ranges. That is the same service the <c>inet</c> column does for a device's address.
    /// </remarks>
    internal static Result<AddressRange> Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DiscoveryErrors.InvalidCidr(value ?? string.Empty);
        }

        string trimmed = value.Trim();
        int slash = trimmed.IndexOf('/', StringComparison.Ordinal);
        string addressPart = slash < 0 ? trimmed : trimmed[..slash];

        if (!IPAddress.TryParse(addressPart, out IPAddress? address)
            || address.AddressFamily is not (AddressFamily.InterNetwork or AddressFamily.InterNetworkV6))
        {
            return DiscoveryErrors.InvalidCidr(trimmed);
        }

        int bits = address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        int prefixLength = bits;

        if (slash >= 0)
        {
            string suffix = trimmed[(slash + 1)..];

            if (!int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out prefixLength)
                || prefixLength < 0
                || prefixLength > bits)
            {
                return DiscoveryErrors.InvalidCidr(trimmed);
            }
        }

        UInt128 number = ToNumber(address);

        // The network mask is the family's own width with the host bits cleared. Written as a
        // subtraction rather than as a shift because a shift by the full width is masked back to
        // no shift at all — which is how a /128 would come out as ::/128 covering everything.
        UInt128 mask = Mask(bits) ^ Mask(bits - prefixLength);

        return new AddressRange(address.AddressFamily, number & mask, prefixLength);
    }

    /// <summary>Whether this block holds <paramref name="address"/>.</summary>
    /// <remarks>
    /// Containment is over the whole block, including the network and broadcast addresses a
    /// sweep would skip: excluding <c>10.0.0.0/24</c> means every address in it, not the 254 that
    /// would have been probed.
    /// </remarks>
    internal bool Contains(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.AddressFamily != Family)
        {
            return false;
        }

        UInt128 number = ToNumber(address);

        return number >= Network && number <= Last;
    }

    /// <summary>Whether this block holds every address of <paramref name="other"/>.</summary>
    internal bool Contains(AddressRange other) =>
        other.Family == Family && other.Network >= Network && other.Last <= Last;

    /// <summary>Whether the two blocks share any address at all.</summary>
    internal bool Overlaps(AddressRange other) =>
        other.Family == Family && other.Network <= Last && Network <= other.Last;

    /// <summary>
    /// The addresses a sweep of this block would probe, cut into spans of at most
    /// <paramref name="maxHosts"/> each — one span being one sweep job's worth of work.
    /// </summary>
    /// <remarks>
    /// A span rather than a smaller CIDR block, because the two do not agree about edges. The
    /// network and broadcast addresses skipped above belong to <em>this</em> block: splitting a
    /// /23 into two /24s would have each half skip its own two edges and drop four addresses
    /// that are ordinary hosts on a /23. A span says exactly which addresses are in the job, so
    /// what the API counted and what the collector probes cannot drift apart.
    /// </remarks>
    internal IEnumerable<AddressSpan> Spans(int maxHosts)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxHosts, 1);

        UInt128 step = (UInt128)maxHosts;
        UInt128 last = LastHost;

        for (UInt128 start = FirstHost; ; start += step)
        {
            UInt128 remaining = last - start;

            if (remaining < step)
            {
                yield return new AddressSpan(Family, start, last);

                yield break;
            }

            yield return new AddressSpan(Family, start, start + step - UInt128.One);
        }
    }

    /// <summary>The block in CIDR notation, which is how it is stored and how it travels.</summary>
    public override string ToString() =>
        $"{ToAddress(Network, Family)}/{PrefixLength.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>An address as a number, big-endian, in the width its family needs.</summary>
    internal static UInt128 ToNumber(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        Span<byte> bytes = stackalloc byte[16];

        if (!address.TryWriteBytes(bytes, out int written))
        {
            throw new ArgumentException("The address could not be read as bytes.", nameof(address));
        }

        UInt128 number = UInt128.Zero;

        for (int index = 0; index < written; index++)
        {
            number = (number << 8) | bytes[index];
        }

        return number;
    }

    /// <summary>A number back to an address of the given family.</summary>
    internal static IPAddress ToAddress(UInt128 number, AddressFamily family)
    {
        int length = family == AddressFamily.InterNetwork ? 4 : 16;

        Span<byte> bytes = stackalloc byte[16];

        for (int index = length - 1; index >= 0; index--)
        {
            bytes[index] = (byte)(number & 0xFF);
            number >>= 8;
        }

        return new IPAddress(bytes[..length]);
    }

    /// <summary>
    /// The low <paramref name="bits"/> bits set, and no others.
    /// </summary>
    /// <remarks>
    /// Both edges are handled explicitly: <c>1 &lt;&lt; 128</c> masks the shift count back to
    /// zero on a <see cref="UInt128"/> and would produce one rather than every bit, and a width
    /// of nothing has to be nothing rather than everything.
    /// </remarks>
    private static UInt128 Mask(int bits) => bits switch
    {
        <= 0 => UInt128.Zero,
        >= 128 => UInt128.MaxValue,
        _ => (UInt128.One << bits) - UInt128.One
    };
}
