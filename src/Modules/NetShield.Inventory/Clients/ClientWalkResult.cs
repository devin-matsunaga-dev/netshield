using System.Text.Json.Serialization;

namespace NetShield.Inventory.Clients;

/// <summary>
/// What a client walk found, as it is written into <c>collector_jobs.result</c>.
/// </summary>
/// <remarks>
/// <para>
/// Two independent readings in one payload, each with its own "did this device answer at all"
/// flag. That distinction is what keeps absence from being read as evidence: an access switch
/// that implements no ARP table answers <see cref="NeighborsSupported"/> false, and a walk that
/// read a router's ARP table and found it empty answers true with an empty list. The first says
/// nothing about the estate and the second says the cache is empty, and the handler must not
/// treat them alike.
/// </para>
/// <para>
/// The truncation flags carry the same weight they do on a fingerprint walk: a reading that hit
/// its ceiling saw part of the table, so nothing may be closed on the strength of its absence.
/// </para>
/// </remarks>
/// <param name="Walk">The discriminator, matching <see cref="ClientWalkParameters.WalkName"/>.</param>
/// <param name="NeighborsSupported">Whether the device answered an ARP or neighbour table at all.</param>
/// <param name="NeighborCount">How many entries that table held, before the report ceiling.</param>
/// <param name="NeighborsTruncated">Whether more entries existed than the result carries.</param>
/// <param name="Neighbors">The address-to-MAC bindings the device's cache held.</param>
/// <param name="ForwardingSupported">Whether the device answered a forwarding database at all.</param>
/// <param name="ForwardingCount">How many entries it held, before the report ceiling.</param>
/// <param name="ForwardingTruncated">Whether more entries existed than the result carries.</param>
/// <param name="Forwarding">The MAC-to-port bindings the device's forwarding database held.</param>
internal sealed record ClientWalkResult(
    [property: JsonPropertyName("walk")] string? Walk,
    [property: JsonPropertyName("neighborsSupported")] bool NeighborsSupported,
    [property: JsonPropertyName("neighborCount")] int NeighborCount,
    [property: JsonPropertyName("neighborsTruncated")] bool NeighborsTruncated,
    [property: JsonPropertyName("neighbors")] IReadOnlyList<ClientWalkNeighbor>? Neighbors,
    [property: JsonPropertyName("forwardingSupported")] bool ForwardingSupported,
    [property: JsonPropertyName("forwardingCount")] int ForwardingCount,
    [property: JsonPropertyName("forwardingTruncated")] bool ForwardingTruncated,
    [property: JsonPropertyName("forwarding")] IReadOnlyList<ClientWalkForwardingEntry>? Forwarding);

/// <summary>One entry of a device's ARP or neighbour cache: an address, on a hardware address.</summary>
/// <param name="IpAddress">The address the device has a binding for.</param>
/// <param name="MacAddress">The hardware address it is bound to, however the agent spelled it.</param>
/// <param name="IfIndex">The device's own index for the interface the binding is on.</param>
internal sealed record ClientWalkNeighbor(
    [property: JsonPropertyName("ipAddress")] string? IpAddress,
    [property: JsonPropertyName("macAddress")] string? MacAddress,
    [property: JsonPropertyName("ifIndex")] int? IfIndex);

/// <summary>One entry of a device's forwarding database: a hardware address, on a port.</summary>
/// <param name="MacAddress">The hardware address the port has learned.</param>
/// <param name="IfIndex">
/// The device's own <c>ifIndex</c> for that port, resolved by the collector from the bridge port
/// number the forwarding table is actually keyed by.
/// </param>
/// <param name="VlanId">
/// The VLAN it was learned on, where the table says. Absent on an agent that implements only the
/// VLAN-unaware forwarding database.
/// </param>
/// <param name="MacCountOnPort">
/// How many MAC addresses that port had learned in this same reading — the evidence that tells
/// an access port from a trunk.
/// </param>
internal sealed record ClientWalkForwardingEntry(
    [property: JsonPropertyName("macAddress")] string? MacAddress,
    [property: JsonPropertyName("ifIndex")] int? IfIndex,
    [property: JsonPropertyName("vlanId")] int? VlanId,
    [property: JsonPropertyName("macCountOnPort")] int? MacCountOnPort);
