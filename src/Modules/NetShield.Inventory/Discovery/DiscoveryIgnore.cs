namespace NetShield.Inventory.Discovery;

/// <summary>
/// An address or range discovery will never offer as a candidate again.
/// </summary>
/// <remarks>
/// <para>
/// Stored as normalised CIDR text rather than as an <c>inet</c> or a <c>cidr</c> column, because
/// every use of it is a containment test — "is this responder inside any ignored block" — and EF
/// cannot express PostgreSQL's containment operators without raw SQL. The list is bounded by
/// what a person types, so it is read into memory once per sweep result and matched there.
/// </para>
/// <para>
/// Permanent by design: WP-1.6's criterion is that an ignored host never reappears. The way back
/// is to delete the entry, which is somebody's decision rather than something a re-run can undo.
/// </para>
/// </remarks>
internal sealed class DiscoveryIgnore
{
    /// <summary>UUID v7.</summary>
    public Guid Id { get; init; }

    /// <summary>
    /// The block, in normalised CIDR notation. Unique — <c>10.0.0.5</c> and <c>10.0.0.5/32</c>
    /// are the same entry, because <see cref="AddressRange.Parse"/> reads them to the same value.
    /// </summary>
    public string Cidr { get; init; } = string.Empty;

    /// <summary>Why it is ignored, as free text.</summary>
    public string? Reason { get; init; }

    /// <summary>When the row was created. UTC.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the row last changed. UTC.</summary>
    public DateTimeOffset UpdatedAt { get; init; }
}
