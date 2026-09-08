using System.Text.Json.Serialization;

namespace NetShield.Inventory.Topology;

/// <summary>
/// What a neighbour walk found, as it is written into <c>collector_jobs.result</c>.
/// </summary>
/// <remarks>
/// <para>
/// Two independent readings in one payload, each with its own "did this device answer at all"
/// flag — the shape WP-1.8's client walk established, and needed here for a sharper reason. This
/// package <em>withdraws</em> an edge that a successful reading no longer contains, because an
/// LLDP table is the device's complete current statement and its agent expires its own entries.
/// So the difference between "CDP answered and holds nothing" and "this is not a Cisco" is the
/// difference between deleting a device's CDP edges and leaving them exactly as they were.
/// </para>
/// <para>
/// The truncation flags carry the same weight, and for the first time in the build they are
/// actually acted on: a reading that hit its ceiling saw part of a table, so nothing may be
/// withdrawn on the strength of its absence.
/// </para>
/// </remarks>
/// <param name="Walk">The discriminator, matching <see cref="NeighborWalkParameters.WalkName"/>.</param>
/// <param name="LocalChassisId">The identity the device advertises for itself over LLDP.</param>
/// <param name="LocalChassisIdKind">What kind of identifier that is.</param>
/// <param name="LocalSystemName">The name it advertises.</param>
/// <param name="LldpSupported">Whether the device answered an LLDP remote table at all.</param>
/// <param name="LldpCount">How many entries it held, before the report ceiling.</param>
/// <param name="LldpTruncated">Whether more entries existed than the result carries.</param>
/// <param name="Lldp">The neighbours LLDP reported.</param>
/// <param name="CdpSupported">Whether the device answered a CDP cache at all.</param>
/// <param name="CdpCount">How many entries it held, before the report ceiling.</param>
/// <param name="CdpTruncated">Whether more entries existed than the result carries.</param>
/// <param name="Cdp">The neighbours CDP reported.</param>
internal sealed record NeighborWalkResult(
    [property: JsonPropertyName("walk")] string? Walk,
    [property: JsonPropertyName("localChassisId")] string? LocalChassisId,
    [property: JsonPropertyName("localChassisIdKind")] string? LocalChassisIdKind,
    [property: JsonPropertyName("localSystemName")] string? LocalSystemName,
    [property: JsonPropertyName("lldpSupported")] bool LldpSupported,
    [property: JsonPropertyName("lldpCount")] int LldpCount,
    [property: JsonPropertyName("lldpTruncated")] bool LldpTruncated,
    [property: JsonPropertyName("lldp")] IReadOnlyList<LldpNeighborEntry>? Lldp,
    [property: JsonPropertyName("cdpSupported")] bool CdpSupported,
    [property: JsonPropertyName("cdpCount")] int CdpCount,
    [property: JsonPropertyName("cdpTruncated")] bool CdpTruncated,
    [property: JsonPropertyName("cdp")] IReadOnlyList<CdpNeighborEntry>? Cdp);

/// <summary>One entry of a device's LLDP remote table.</summary>
/// <param name="LocalIfIndex">
/// The interface the neighbour was heard on, already joined back from <c>lldpLocPortNum</c> by
/// the collector — which is where the join belongs, since the evidence for it is on the device.
/// </param>
/// <param name="LocalPortName">What the observing device calls that interface.</param>
/// <param name="ChassisId">The neighbour's identity, however it spelled it.</param>
/// <param name="ChassisIdKind">Which of the 802.1AB subtypes that is, resolved to a name.</param>
/// <param name="PortId">The neighbour's port.</param>
/// <param name="PortIdKind">Which port subtype that is.</param>
/// <param name="PortDescription">The neighbour's description of its port.</param>
/// <param name="SystemName">What the neighbour calls itself.</param>
/// <param name="SystemDescription">What it says it is.</param>
/// <param name="ManagementAddress">The management address it advertised.</param>
/// <param name="Capabilities">The capability bits it advertised.</param>
internal sealed record LldpNeighborEntry(
    [property: JsonPropertyName("localIfIndex")] int? LocalIfIndex,
    [property: JsonPropertyName("localPortName")] string? LocalPortName,
    [property: JsonPropertyName("chassisId")] string? ChassisId,
    [property: JsonPropertyName("chassisIdKind")] string? ChassisIdKind,
    [property: JsonPropertyName("portId")] string? PortId,
    [property: JsonPropertyName("portIdKind")] string? PortIdKind,
    [property: JsonPropertyName("portDescription")] string? PortDescription,
    [property: JsonPropertyName("systemName")] string? SystemName,
    [property: JsonPropertyName("systemDescription")] string? SystemDescription,
    [property: JsonPropertyName("managementAddress")] string? ManagementAddress,
    [property: JsonPropertyName("capabilities")] int? Capabilities);

/// <summary>One entry of a Cisco device's CDP cache.</summary>
/// <param name="LocalIfIndex">The interface it was heard on. Straight from the table's index.</param>
/// <param name="LocalPortName">What the observing device calls that interface.</param>
/// <param name="DeviceId">
/// What CDP calls the neighbour — usually a host name, occasionally a serial. Not an identity,
/// which is why LLDP outranks this where the two disagree about one port.
/// </param>
/// <param name="DevicePort">The neighbour's port, in the neighbour's own spelling.</param>
/// <param name="Platform">The platform it reported.</param>
/// <param name="Version">The software version it reported.</param>
/// <param name="Address">Its address, reassembled from the raw octets the column carries.</param>
/// <param name="Capabilities">The capability bits it advertised.</param>
internal sealed record CdpNeighborEntry(
    [property: JsonPropertyName("localIfIndex")] int? LocalIfIndex,
    [property: JsonPropertyName("localPortName")] string? LocalPortName,
    [property: JsonPropertyName("deviceId")] string? DeviceId,
    [property: JsonPropertyName("devicePort")] string? DevicePort,
    [property: JsonPropertyName("platform")] string? Platform,
    [property: JsonPropertyName("version")] string? Version,
    [property: JsonPropertyName("address")] string? Address,
    [property: JsonPropertyName("capabilities")] int? Capabilities);
