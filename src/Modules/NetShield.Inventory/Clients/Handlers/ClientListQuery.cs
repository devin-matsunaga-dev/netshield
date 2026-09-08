namespace NetShield.Inventory.Clients.Handlers;

/// <summary>
/// What a caller can narrow the client list by.
/// </summary>
/// <remarks>
/// Deliberately smaller than the device list's eight filters. A device carries attributes an
/// operator maintains — site, role, criticality, environment, tags — and a client carries only
/// what was observed, so there is nothing here to filter on that somebody chose. What is here is
/// the four questions the observations can actually answer: which endpoint, whose port, which
/// VLAN, and how recently.
/// </remarks>
/// <param name="Search">
/// A MAC address in any spelling, an IP address, or the start of a hostname. One box, because a
/// person looking for a client has exactly one of those three and should not have to say which.
/// </param>
/// <param name="DeviceId">Only clients a given device's port reports now.</param>
/// <param name="VlanId">Only clients on a given VLAN now.</param>
/// <param name="SeenSince">Only clients an observation has confirmed since this instant.</param>
/// <param name="OnlyActive">
/// Only clients that hold an address now. A client seen in a forwarding database and never in an
/// ARP table has a port and no address, which is a real and ordinary state — this is how a reader
/// asks for the ones that can actually be attributed to traffic.
/// </param>
internal sealed record ClientListQuery(
    string? Search = null,
    Guid? DeviceId = null,
    int? VlanId = null,
    DateTimeOffset? SeenSince = null,
    bool OnlyActive = false);
