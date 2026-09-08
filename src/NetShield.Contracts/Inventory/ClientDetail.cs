namespace NetShield.Contracts.Inventory;

/// <summary>
/// One tracked client, with everything that is true about it right now.
/// </summary>
/// <remarks>
/// The detail differs from <see cref="ClientSummary"/> in carrying <em>every</em> current
/// binding rather than one of each. A client can hold an IPv4 and an IPv6 address at once, and a
/// MAC is learned by every switch on the path to it — not only by the one whose access port it
/// is plugged into — so "which port is this on" has several honest answers until Phase 2's
/// topology can tell an edge port from an uplink. <c>MacCountOnPort</c> on each port binding is
/// the evidence a reader can use in the meantime: a port that learned one MAC is an access port,
/// and a port that learned two hundred is a trunk.
/// </remarks>
/// <param name="Id">The client row.</param>
/// <param name="MacAddress">The MAC, as colon-separated uppercase hex.</param>
/// <param name="Oui">The first three octets — the IEEE Organizationally Unique Identifier.</param>
/// <param name="LocallyAdministered">Whether the address's local bit is set.</param>
/// <param name="Hostname">What the client calls itself, when something has said.</param>
/// <param name="IpBindings">Every address it holds now, oldest binding first.</param>
/// <param name="PortBindings">Every port that reports it now, oldest binding first.</param>
/// <param name="FirstSeenAt">When any observation first named this MAC. UTC.</param>
/// <param name="LastSeenAt">When one last did. UTC.</param>
public sealed record ClientDetail(
    Guid Id,
    string MacAddress,
    string Oui,
    bool LocallyAdministered,
    string? Hostname,
    IReadOnlyList<ClientIpBindingSummary> IpBindings,
    IReadOnlyList<ClientPortBindingSummary> PortBindings,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt);
