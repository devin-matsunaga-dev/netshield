namespace NetShield.Inventory.Topology;

/// <summary>
/// What NetShield knows about reading one device's topology, and when it will ask again.
/// </summary>
/// <remarks>
/// <para>
/// The same shape as <c>device_reachability</c>, <c>device_fingerprints</c> and
/// <c>device_client_scans</c>, and there for the same three reasons: the schedule needs somewhere
/// to record when a device is next due, each result handler needs somewhere to record the job it
/// last applied so that an at-least-once redelivery is a no-op, and a walk that could not be
/// performed needs somewhere to say so that is not the adjacency table itself.
/// </para>
/// <para>
/// <strong>One row, three walks.</strong> The neighbour read, the route read and the VLAN read
/// are separate jobs with separate schedules and separate failure modes, and each has its own due
/// time, its own last-applied job and its own error here. They share a row because they share a
/// device and because the question an operator asks — "why does this switch show no edges and no
/// VLANs?" — is answered by all three at once.
/// </para>
/// </remarks>
internal sealed class DeviceTopologyScan
{
    /// <summary>UUID v7.</summary>
    public Guid Id { get; init; }

    /// <summary>The device this is about. Unique — a device has one scan row.</summary>
    public Guid DeviceId { get; init; }

    // --- The neighbour walk: LLDP, and CDP where the vendor has it. ---

    /// <summary>The earliest the next neighbour walk should be queued. UTC.</summary>
    public DateTimeOffset NextNeighborWalkAt { get; set; }

    /// <summary>When a neighbour walk last reported, successfully or not. UTC.</summary>
    public DateTimeOffset? LastNeighborWalkAt { get; set; }

    /// <summary>The neighbour walk whose result was last applied to this device's edges.</summary>
    public Guid? LastNeighborJobId { get; set; }

    /// <summary>Whether the device answered an LLDP remote table at the last walk.</summary>
    public bool? LldpSupported { get; set; }

    /// <summary>Whether it answered a CDP cache.</summary>
    public bool? CdpSupported { get; set; }

    /// <summary>How many LLDP neighbours the last walk read.</summary>
    public int? LastLldpCount { get; set; }

    /// <summary>How many CDP neighbours it read.</summary>
    public int? LastCdpCount { get; set; }

    /// <summary>Why the last neighbour walk could not be performed, where it could not.</summary>
    public string? LastNeighborError { get; set; }

    // --- The route walk: the L3 half, on its own clock. ---

    /// <summary>The earliest the next route walk should be queued. UTC.</summary>
    public DateTimeOffset NextRouteWalkAt { get; set; }

    /// <summary>When a route walk last reported, successfully or not. UTC.</summary>
    public DateTimeOffset? LastRouteWalkAt { get; set; }

    /// <summary>The route walk whose result was last applied.</summary>
    public Guid? LastRouteJobId { get; set; }

    /// <summary>Whether the device answered a routing table at all.</summary>
    public bool? RoutingSupported { get; set; }

    /// <summary>Which of the three tables answered, where one did.</summary>
    public string? RouteTable { get; set; }

    /// <summary>How many routes it held, before the reduction to distinct gateways.</summary>
    public int? LastRouteCount { get; set; }

    /// <summary>How many distinct gateways they reduced to.</summary>
    public int? LastNextHopCount { get; set; }

    /// <summary>Why the last route walk could not be performed, where it could not.</summary>
    public string? LastRouteError { get; set; }

    // --- The VLAN walk: the Q-BRIDGE half, on the slowest clock of the three. ---

    /// <summary>The earliest the next VLAN walk should be queued. UTC.</summary>
    public DateTimeOffset NextVlanWalkAt { get; set; }

    /// <summary>When a VLAN walk last reported, successfully or not. UTC.</summary>
    public DateTimeOffset? LastVlanWalkAt { get; set; }

    /// <summary>The VLAN walk whose result was last applied to this device's VLAN rows.</summary>
    public Guid? LastVlanJobId { get; set; }

    /// <summary>Whether the device answered a Q-BRIDGE VLAN table at all.</summary>
    public bool? VlansSupported { get; set; }

    /// <summary>Which of the two tables answered, where one did.</summary>
    public string? VlanTable { get; set; }

    /// <summary>How many VLANs the last walk read.</summary>
    public int? LastVlanCount { get; set; }

    /// <summary>Why the last VLAN walk could not be performed, where it could not.</summary>
    public string? LastVlanError { get; set; }

    /// <summary>UTC.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>UTC.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
