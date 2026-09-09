namespace NetShield.Contracts.Inventory;

/// <summary>
/// One switch port and what is on the other end of it.
/// </summary>
/// <remarks>
/// <para>
/// A join over three packages' data and no new collection: the topology edge where the far end is
/// a device NetShield monitors (WP-2.1), the LLDP or CDP neighbour where it is an unmanaged
/// speaker, and the learned MAC addresses where it is a silent host (WP-1.8). All three are
/// keyed on <c>(device, ifIndex)</c> and nothing has read them together before.
/// </para>
/// <para>
/// <strong>A port here is not only an interface.</strong> The list is every <c>ifIndex</c> the
/// device has an interface row for <em>or</em> that any observation names, because a switch can
/// forward on a port whose interface row a fingerprint walk has not recorded yet — and a port
/// that is invisible because the fingerprint is behind is worse than one with no name.
/// <see cref="InterfaceKnown"/> says which of the two a row is.
/// </para>
/// <para>
/// It is a read of conclusions already drawn, not a walk: nothing here asks a device anything,
/// and there is no write route, because what is plugged into a port is observed rather than
/// declared.
/// </para>
/// </remarks>
/// <param name="IfIndex">The device's own index for the port. Unique per device, and the page's key.</param>
/// <param name="Name"><c>ifName</c>, where a walk has recorded the interface.</param>
/// <param name="Description"><c>ifDescr</c>.</param>
/// <param name="Alias"><c>ifAlias</c> — whatever an operator configured on the device itself.</param>
/// <param name="InterfaceKnown">
/// Whether a fingerprint walk has recorded this interface. False for a port only an observation
/// names, whose interface fields are therefore all absent and whose statuses read
/// <c>Unknown</c>.
/// </param>
/// <param name="AdminStatus">What the interface is configured to do. <c>Unknown</c> where it is not recorded.</param>
/// <param name="OperStatus">What it is actually doing. <c>Unknown</c> where it is not recorded.</param>
/// <param name="Role">Whether this holds endpoints, carries what is behind it, or neither.</param>
/// <param name="RoleReason">What decided that, so the classification can be argued with.</param>
/// <param name="LearnedAddressCount">
/// How many MAC addresses the port had learned when it was last read — the switch's own count,
/// from the forwarding database the binding came out of, not a count of what NetShield holds.
/// Absent on an agent that answers only the VLAN-unaware table, where the count is taken from
/// the bindings instead.
/// </param>
/// <param name="ClientCount">How many open client bindings NetShield holds on this port.</param>
/// <param name="ClientsListed">
/// Whether <paramref name="Clients"/> is the occupants or is deliberately empty. False on an
/// uplink, where the addresses are carried rather than attached.
/// </param>
/// <param name="Neighbors">What announced itself on the port. Empty where nothing did.</param>
/// <param name="Clients">
/// The silent hosts on the port, when <paramref name="ClientsListed"/>. Empty otherwise, and an
/// empty list there means "not listed" rather than "none".
/// </param>
public sealed record DevicePortSummary(
    int IfIndex,
    string? Name,
    string? Description,
    string? Alias,
    bool InterfaceKnown,
    InterfaceStatus AdminStatus,
    InterfaceStatus OperStatus,
    PortRole Role,
    PortRoleReason RoleReason,
    int? LearnedAddressCount,
    int ClientCount,
    bool ClientsListed,
    IReadOnlyList<PortNeighbor> Neighbors,
    IReadOnlyList<PortClient> Clients);
