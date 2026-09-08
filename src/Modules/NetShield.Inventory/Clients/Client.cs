namespace NetShield.Inventory.Clients;

/// <summary>
/// One endpoint NetShield has observed on the network, identified by its hardware address.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The MAC is the identity and nothing else is.</strong> An address is a lease, a port is
/// a cable, and a hostname is whatever a device chose to say; the hardware address is the one
/// thing that survives all three changing, which is why the two history tables hang off this row
/// rather than the other way round. It is stored normalised
/// (<see cref="MacAddress.Normalize"/>) and uniquely indexed, so one endpoint is one row however
/// the observation that found it happened to spell the address.
/// </para>
/// <para>
/// <strong>Nothing current is stored here.</strong> There is no <c>current_ip_address</c> and no
/// <c>current_port</c>: the current binding is the open interval in
/// <see cref="ClientIpBinding"/> or <see cref="ClientPortBinding"/>, and a denormalised copy
/// beside it would be a second answer to a question that already has one. The cost is a join on
/// the list screen; the benefit is that "what does this client hold" cannot be answered two
/// different ways.
/// </para>
/// <para>
/// <strong>Derived data, not inventory.</strong> Every column is reconstructible by reading the
/// estate's tables again, and nothing an operator types reaches it — so it is not soft-deleted,
/// for the reason <c>device_interfaces</c> is not (CONVENTIONS.md §3 puts soft delete on the
/// inventory an operator maintains). What retention eventually prunes is a policy decision the
/// Phase 8 table owns.
/// </para>
/// </remarks>
internal sealed class Client
{
    /// <summary>UUID v7, so the primary key is also the order clients were first seen in.</summary>
    public Guid Id { get; init; }

    /// <summary>The hardware address, normalised to colon-separated uppercase hex. Unique.</summary>
    public required string MacAddress { get; init; }

    /// <summary>
    /// The first three octets — the IEEE Organizationally Unique Identifier. Stored because it
    /// is a fact about the address; the vendor name it stands for is deliberately not resolved.
    /// </summary>
    public required string Oui { get; init; }

    /// <summary>
    /// Whether the address's local bit is set. A locally administered address stands for no
    /// vendor at all, which is what MAC randomisation on a phone looks like.
    /// </summary>
    public bool LocallyAdministered { get; init; }

    /// <summary>
    /// What the client calls itself, when something has said.
    /// </summary>
    /// <remarks>
    /// Nothing writes this in V1. SPEC.md §2 names a hostname in the client identity record and
    /// the two sources that would supply one — a DHCP lease and a wireless association — are
    /// declared and unproduced (<c>ClientObservationSource</c>). The column is here so that the
    /// package which finds a real source writes a producer rather than a migration.
    /// </remarks>
    public string? Hostname { get; set; }

    /// <summary>When any observation first named this address. UTC, and never rewritten.</summary>
    public DateTimeOffset FirstSeenAt { get; init; }

    /// <summary>When an observation last named it. UTC.</summary>
    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>When the row was created. UTC.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the row last changed. UTC.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
