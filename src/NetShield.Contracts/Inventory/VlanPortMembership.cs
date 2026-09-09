namespace NetShield.Contracts.Inventory;

/// <summary>
/// One port a VLAN is configured on, as one device reported it.
/// </summary>
/// <remarks>
/// <para>
/// The interface index is what the device itself said; the name is joined from the interface
/// inventory WP-1.5 records and is <see langword="null"/> where no walk has recorded that
/// interface yet. A switch can carry a VLAN on a port whose interface row is behind, and a
/// membership that could not be named is still a membership.
/// </para>
/// <para>
/// <see cref="Untagged"/> is the part that means something operationally: a tagged member is a
/// trunk carrying the VLAN past this device, and an untagged one is where something is plugged
/// into it.
/// </para>
/// </remarks>
/// <param name="IfIndex">The device's own index for the port.</param>
/// <param name="InterfaceName">What the device calls it, where the inventory knows.</param>
/// <param name="Untagged">Whether the VLAN leaves this port untagged.</param>
public sealed record VlanPortMembership(
    int IfIndex,
    string? InterfaceName,
    bool Untagged);
