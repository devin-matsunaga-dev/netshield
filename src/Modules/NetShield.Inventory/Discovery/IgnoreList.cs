using System.Net;

namespace NetShield.Inventory.Discovery;

/// <summary>
/// The blocks discovery will never offer as candidates, in a form a sweep result can be matched
/// against.
/// </summary>
/// <remarks>
/// <para>
/// Read from <c>discovery_ignores</c> once per sweep result rather than queried per address: a
/// job reports up to a few hundred responders and the ignore list is bounded by what a person
/// has typed, so one read and a linear scan is cheaper than a round trip each. It is also the
/// only way to do it without raw SQL — the test is address-in-block, which is PostgreSQL's
/// <c>&lt;&lt;=</c> and not something EF can express.
/// </para>
/// <para>
/// A stored entry that will not parse is skipped rather than thrown on. The column is written
/// only through <see cref="AddressRange.Parse"/>, so a malformed row means somebody edited the
/// database by hand — and failing the whole sweep result over it would lose the run.
/// </para>
/// </remarks>
internal sealed class IgnoreList
{
    private readonly IReadOnlyList<AddressRange> blocks;

    private IgnoreList(IReadOnlyList<AddressRange> blocks) => this.blocks = blocks;

    /// <summary>An empty list, which ignores nothing.</summary>
    internal static IgnoreList Empty { get; } = new([]);

    /// <summary>Reads the stored entries, skipping any that will not parse.</summary>
    internal static IgnoreList From(IEnumerable<string> cidrs)
    {
        ArgumentNullException.ThrowIfNull(cidrs);

        List<AddressRange> parsed = [];

        foreach (string cidr in cidrs)
        {
            if (AddressRange.Parse(cidr) is { IsSuccess: true } block)
            {
                parsed.Add(block.Value);
            }
        }

        return new IgnoreList(parsed);
    }

    /// <summary>Whether any ignored block holds this address.</summary>
    internal bool Contains(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        foreach (AddressRange block in blocks)
        {
            if (block.Contains(address))
            {
                return true;
            }
        }

        return false;
    }
}
