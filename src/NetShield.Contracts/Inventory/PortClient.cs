namespace NetShield.Contracts.Inventory;

/// <summary>
/// One silent host on a port: something whose only evidence is that a switch learned its address.
/// </summary>
/// <remarks>
/// <para>
/// The third of the three kinds of occupant, and the one that says least about itself. A laptop
/// announces nothing — it is on the port because a forwarding database learned its MAC there, and
/// the address, the VLAN and the OUI are the whole of what is known.
/// </para>
/// <para>
/// <strong>These are listed only on an access port.</strong> A MAC is learned by every bridge on
/// the path to it, so an uplink's forwarding database holds every host beyond it; listing those
/// as occupants of the uplink would say they are plugged into it, which is exactly what they are
/// not. <c>PortRole</c> decides, and an uplink counts instead.
/// </para>
/// <para>
/// Which of several ports a client is <em>genuinely</em> on is a different question, still open:
/// this is what each port reported, not an adjudication between them.
/// </para>
/// </remarks>
/// <param name="ClientId">The client, so a reader can open its history.</param>
/// <param name="MacAddress">The hardware address, normalised. The client's identity.</param>
/// <param name="Hostname">What the client calls itself, where anything has said. Nothing writes this yet.</param>
/// <param name="Oui">The first three octets — the IEEE Organizationally Unique Identifier.</param>
/// <param name="LocallyAdministered">
/// Whether the address stands for no vendor at all, which is what address randomisation on a
/// phone looks like.
/// </param>
/// <param name="IpAddress">The address the client currently holds, where it holds one.</param>
/// <param name="VlanId">The VLAN the port learned it on, where the device's table said which.</param>
/// <param name="ObservedFrom">When this port's binding opened. UTC.</param>
/// <param name="LastSeenAt">When an observation last confirmed it. UTC.</param>
public sealed record PortClient(
    Guid ClientId,
    string MacAddress,
    string? Hostname,
    string Oui,
    bool LocallyAdministered,
    string? IpAddress,
    int? VlanId,
    DateTimeOffset ObservedFrom,
    DateTimeOffset LastSeenAt);
