using System.Text.Json.Serialization;

namespace NetShield.Contracts.Inventory;

/// <summary>
/// What a switch port is doing: holding an endpoint, carrying everything behind it, or nothing.
/// </summary>
/// <remarks>
/// <para>
/// This is the distinction the whole port view turns on. A MAC address is learned by every bridge
/// on the path to it, so an uplink's forwarding database contains every host beyond it — and a
/// screen that listed those two hundred addresses as things plugged into that port would be
/// actively misleading about where they are. An <see cref="Uplink"/> says how many addresses it
/// carries; an <see cref="Access"/> port says what is on it.
/// </para>
/// <para>
/// It is a classification rather than a fact the network states, which is why
/// <c>PortRoleReason</c> travels beside it: an operator disagreeing with the answer should be
/// able to see what produced it.
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<PortRole>))]
public enum PortRole
{
    /// <summary>Nothing has been observed on the port.</summary>
    Empty,

    /// <summary>Endpoints are on the port, and they are listed.</summary>
    Access,

    /// <summary>The port carries what is behind it. Addresses are counted rather than listed.</summary>
    Uplink
}
