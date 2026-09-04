namespace NetShield.Inventory.Collector;

/// <summary>
/// A collector process that has said hello, and what it said about itself.
/// </summary>
/// <remarks>
/// <para>
/// Named <c>CollectorNode</c> rather than <c>Collector</c> because the feature namespace is
/// already called that; the table it maps to is <c>collectors</c>, which is what the domain
/// calls them.
/// </para>
/// <para>
/// Every column here is the collector's own claim about itself, taken at face value. The shared
/// secret proves that a collector is talking, not which one — so this row says what the fleet
/// reports, and nothing in the system makes an authorization decision from it. Its purpose is
/// the system-health page in Phase 8 and an operator asking "is anything collecting".
/// </para>
/// </remarks>
internal sealed class CollectorNode
{
    /// <summary>UUID v7.</summary>
    public Guid Id { get; init; }

    /// <summary>What the collector calls itself, as it last reported it.</summary>
    public required string Name { get; set; }

    /// <summary>
    /// <see cref="Name"/> case-folded, which is what one collector is identified as across
    /// restarts — a heartbeat updates the row rather than adding another.
    /// </summary>
    public required string NormalizedName { get; init; }

    /// <summary>The version it reported.</summary>
    public string? Version { get; set; }

    /// <summary>How many jobs it says it can run at once.</summary>
    public int Capacity { get; set; }

    /// <summary>How many it says it is running now.</summary>
    public int Running { get; set; }

    /// <summary>When it last reported. UTC — this is the liveness signal.</summary>
    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>When it first reported. UTC.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the row last changed. UTC.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
