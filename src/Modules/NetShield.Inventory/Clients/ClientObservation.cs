using System.Net;

using NetShield.Contracts.Inventory;

namespace NetShield.Inventory.Clients;

/// <summary>
/// One thing an observation said: this hardware address held this IP address, or was reachable
/// through this port.
/// </summary>
/// <remarks>
/// The shape the walk result is reduced to before any of it reaches the database. Keeping it
/// separate from <c>ClientWalkResult</c> is what lets the binding logic be tested without a
/// collector payload, and what will let a second producer — a DHCP lease reader, a wireless
/// association reader — feed the same code rather than writing its own interval arithmetic.
/// </remarks>
internal static class ClientObservation
{
    /// <summary>An address seen bound to a hardware address.</summary>
    /// <param name="MacAddress">The hardware address, already normalised.</param>
    /// <param name="IpAddress">The address it held.</param>
    /// <param name="Source">What kind of observation this was.</param>
    internal sealed record Address(string MacAddress, IPAddress IpAddress, ClientObservationSource Source);

    /// <summary>A hardware address seen on a device's port.</summary>
    /// <param name="MacAddress">The hardware address, already normalised.</param>
    /// <param name="IfIndex">The device's own index for the port.</param>
    /// <param name="VlanId">The VLAN it was learned on, where the table says.</param>
    /// <param name="MacCountOnPort">How many addresses that port had learned in this reading.</param>
    /// <param name="Source">What kind of observation this was.</param>
    internal sealed record Port(
        string MacAddress,
        int IfIndex,
        int? VlanId,
        int? MacCountOnPort,
        ClientObservationSource Source);
}
