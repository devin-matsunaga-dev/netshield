namespace NetShield.Inventory.Discovery;

/// <summary>
/// What NetShield sweeps looking for hosts nobody has entered, and how often.
/// </summary>
/// <remarks>
/// <para>
/// The one piece of discovery an operator maintains by hand, which is why it soft-deletes and
/// the tables that record what it found do not: a run, a per-host observation and a candidate
/// are all machine output, kept until a retention policy prunes them.
/// </para>
/// <para>
/// A seed names no credential. Which credential a device is walked with is decided by
/// <c>DiscoveryOptions.CredentialKindOrder</c> when the walk is queued, so that a caller holding
/// <c>PoliciesWrite</c> cannot decide what a collector will be handed — WP-1.2 put that behind
/// <c>CredentialsManage</c> and a seed is not a way around it.
/// </para>
/// </remarks>
internal sealed class DiscoverySeed
{
    /// <summary>UUID v7, so the primary key is also the order seeds were created in.</summary>
    public Guid Id { get; init; }

    /// <summary>What an operator calls it. Unique among live seeds.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Why it exists, as free text.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// Whether the schedule runs it. An on-demand run ignores this: turning a seed off stops it
    /// running on its own, and does not stop a person asking.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>The CIDR blocks it sweeps, normalised. Never overlapping each other.</summary>
    public IReadOnlyList<string> Ranges { get; set; } = [];

    /// <summary>Blocks inside those that are never probed, normalised.</summary>
    public IReadOnlyList<string> Exclusions { get; set; } = [];

    /// <summary>How often the schedule runs it.</summary>
    public int IntervalMinutes { get; set; }

    /// <summary>
    /// When the schedule may next start a run, or <see langword="null"/> if it never has.
    /// </summary>
    /// <remarks>
    /// Null means due now, the same convention the reachability schedule uses for a device with
    /// no reachability row: a seed nobody has swept is the most interesting thing in the estate,
    /// not the least.
    /// </remarks>
    public DateTimeOffset? NextRunAt { get; set; }

    /// <summary>When a run of this seed last started. UTC.</summary>
    public DateTimeOffset? LastRunAt { get; set; }

    /// <summary>When the row was created. UTC.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the row last changed. UTC.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>When it was removed, or <see langword="null"/> while it is live.</summary>
    public DateTimeOffset? DeletedAt { get; set; }
}
