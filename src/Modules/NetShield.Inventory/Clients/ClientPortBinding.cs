using NetShield.Contracts.Inventory;

namespace NetShield.Inventory.Clients;

/// <summary>
/// One period during which one device's port reported one client, as a half-open interval.
/// </summary>
/// <remarks>
/// <para>
/// Shaped like <see cref="ClientIpBinding"/> and closed by the same rule, with one difference
/// that decides the schema: <strong>the open interval is unique per client and device, not per
/// client.</strong> A MAC is learned by every switch on the path to it, so a laptop plugged into
/// an access switch appears in that switch's forwarding database on its access port
/// <em>and</em> in every upstream switch's on the uplink carrying it. All of those are true at
/// once. Enforcing one open port binding per client would mean each walk closing another
/// switch's correct observation, and the client would appear to move between switches on every
/// scan.
/// </para>
/// <para>
/// Which of them is the edge port is a topology question, and topology is Phase 2's.
/// <see cref="MacCountOnPort"/> is what makes the answer readable before then: it is counted from
/// the same forwarding database this binding came out of, so it is evidence rather than a guess
/// — a port that had learned one MAC is where something is plugged in, and a port that had
/// learned two hundred is a trunk carrying everything behind it.
/// </para>
/// </remarks>
internal sealed class ClientPortBinding
{
    /// <summary>UUID v7.</summary>
    public Guid Id { get; init; }

    /// <summary>The client the port reported.</summary>
    public Guid ClientId { get; init; }

    /// <summary>The device whose forwarding database reported it.</summary>
    public Guid DeviceId { get; init; }

    /// <summary>
    /// That device's own <c>ifIndex</c> for the port. Not a foreign key to
    /// <c>device_interfaces</c>: a switch can forward on a port whose interface row a walk has
    /// not recorded yet, and a binding that could not be written because the fingerprint was
    /// behind would lose the observation rather than the join.
    /// </summary>
    public int IfIndex { get; set; }

    /// <summary>
    /// The VLAN the port learned it on, where the device's table says which. Absent on an agent
    /// that implements only the VLAN-unaware forwarding database.
    /// </summary>
    public int? VlanId { get; set; }

    /// <summary>
    /// How many MAC addresses that port had learned when this observation was made — the
    /// evidence that tells an access port from a trunk.
    /// </summary>
    public int? MacCountOnPort { get; set; }

    /// <summary>What kind of observation established this binding.</summary>
    public ClientObservationSource Source { get; set; }

    /// <summary>When the interval opened. UTC, inclusive.</summary>
    public DateTimeOffset ObservedFrom { get; init; }

    /// <summary>When it closed, or <see langword="null"/> while it is current. UTC, exclusive.</summary>
    public DateTimeOffset? ObservedTo { get; set; }

    /// <summary>When an observation last confirmed it. UTC.</summary>
    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>When the row was created. UTC.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the row last changed. UTC.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
