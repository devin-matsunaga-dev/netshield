using System.Text.Json.Serialization;

namespace NetShield.Contracts.Inventory;

/// <summary>
/// What kind of thing a neighbour identifier is.
/// </summary>
/// <remarks>
/// <para>
/// IEEE 802.1AB lets a device identify itself and its ports several ways, and the two
/// enumerations it defines share no numbering — <c>4</c> is a MAC address on a chassis id and a
/// network address on a port id. The collector resolves each against its own table and sends the
/// <em>name</em>, so nothing downstream has to know which of the two a number came from.
/// </para>
/// <para>
/// A reader needs it because it says how much the identifier is worth: a
/// <see cref="MacAddress"/> chassis id can be matched against an interface NetShield has already
/// inventoried, and a <see cref="Local"/> one is whatever the vendor felt like.
/// </para>
/// <para>
/// <see cref="Unknown"/> is the member an absent or unrecognised subtype maps to. It is a real
/// value rather than a null, deliberately: a nullable enum member makes ASP.NET Core append
/// <c>null</c> to the <em>shared</em> enum schema, so every other shape using this type would
/// read as nullable in the generated client although it never is (WP-1.8).
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<NeighborIdKind>))]
public enum NeighborIdKind
{
    /// <summary>The device gave no subtype, or one this build does not name.</summary>
    Unknown,

    /// <summary>A component of the chassis, in the vendor's own terms.</summary>
    ChassisComponent,

    /// <summary>An interface alias — the description an operator configured.</summary>
    InterfaceAlias,

    /// <summary>A port component, in the vendor's own terms.</summary>
    PortComponent,

    /// <summary>A hardware address. The most useful kind: it can be matched to an inventoried interface.</summary>
    MacAddress,

    /// <summary>An IP address.</summary>
    NetworkAddress,

    /// <summary>An interface name, as the device spells it.</summary>
    InterfaceName,

    /// <summary>An agent circuit id, from DHCP relay.</summary>
    AgentCircuitId,

    /// <summary>Whatever the vendor chose. Locally significant, and comparable to nothing.</summary>
    Local,

    /// <summary>
    /// A CDP device id, which is usually a host name and occasionally a serial number. Not an
    /// 802.1AB subtype at all — it is here because CDP identifies a neighbour by something that
    /// has no subtype, and a reader deserves to be told which of the two protocols they are
    /// looking at the identity from.
    /// </summary>
    DeviceId
}
