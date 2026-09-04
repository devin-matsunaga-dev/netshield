namespace NetShield.Contracts.Inventory;

/// <summary>
/// An address or range discovery will never offer as a candidate again.
/// </summary>
/// <remarks>
/// Permanent, and deliberately so: WP-1.6's criterion is that an ignored host never reappears.
/// The way back is to delete the entry, which is a decision somebody makes rather than something
/// a re-run can undo.
/// </remarks>
/// <param name="Id">The entry.</param>
/// <param name="Cidr">
/// The address or range, in CIDR notation. A single address is written with its full prefix, so
/// <c>10.0.0.5</c> is stored and returned as <c>10.0.0.5/32</c>.
/// </param>
/// <param name="Reason">Why it is ignored, as free text.</param>
/// <param name="CreatedAt">When it was added. UTC.</param>
public sealed record DiscoveryIgnoreEntry(
    Guid Id,
    string Cidr,
    string? Reason,
    DateTimeOffset CreatedAt);
