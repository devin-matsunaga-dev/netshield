using System.Text.Json.Serialization;

namespace NetShield.Contracts.Inventory;

/// <summary>
/// What an endpoint says it <em>is</em>, in its own words.
/// </summary>
/// <remarks>
/// <para>
/// The vocabulary is IEEE 802.1AB's <c>LldpSystemCapabilitiesMap</c>, and it is the only
/// authoritative statement of an endpoint's kind NetShield collects: everything else is an
/// inference from an OUI, a VLAN or a host name. An access point, an IP phone and a great many
/// cameras all advertise this and mean it.
/// </para>
/// <para>
/// <strong>A CDP capability word is mapped into the same vocabulary, not a second one.</strong>
/// CISCO-CDP-MIB numbers different bits with different meanings, so it is decoded by its own
/// table and then said in these words — a switch is a <see cref="Bridge"/> and a host is a
/// <see cref="Station"/>. Two of its bits are dropped rather than translated: <c>igmpCapable</c>
/// is a protocol feature rather than a kind of device, and a source-route bridge is reported as
/// a <see cref="Bridge"/> because the distinction is about how it forwards rather than about
/// what it is.
/// </para>
/// <para>
/// These are what the endpoint <em>enabled</em>, not what it is capable of. A router with routing
/// switched off does not advertise <see cref="Router"/>.
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<SystemCapability>))]
public enum SystemCapability
{
    /// <summary>Something the standard's other seven members do not name.</summary>
    Other,

    /// <summary>A repeater or hub.</summary>
    Repeater,

    /// <summary>A bridge — which is what a switch advertises.</summary>
    Bridge,

    /// <summary>A wireless access point.</summary>
    WlanAccessPoint,

    /// <summary>A router.</summary>
    Router,

    /// <summary>A telephone.</summary>
    Telephone,

    /// <summary>A DOCSIS cable device.</summary>
    DocsisCableDevice,

    /// <summary>An end station and nothing else — a workstation, a server, a printer.</summary>
    Station
}
