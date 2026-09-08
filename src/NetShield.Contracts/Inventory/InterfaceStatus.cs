using System.Text.Json.Serialization;

namespace NetShield.Contracts.Inventory;

/// <summary>
/// What an interface says about itself — the administrative intent, or the operational reality.
/// </summary>
/// <remarks>
/// <para>
/// The collector records IF-MIB's <c>ifAdminStatus</c> and <c>ifOperStatus</c> as the raw
/// integers the device answered with, and the row keeps them: a diagnostic wants to know that a
/// device said <c>9</c>, not that NetShield gave up on it. This is the same fact named, so that
/// the wire contract and the screen speak about interfaces rather than about SNMP.
/// </para>
/// <para>
/// <c>ifAdminStatus</c> defines only the first three members; the rest are
/// <c>ifOperStatus</c>'s. A value neither defines maps to <see cref="Unknown"/>, which is also
/// what an interface that answered neither reads as — the distinction between "said something
/// unrecognised" and "said nothing" is not one a screen can act on differently.
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<InterfaceStatus>))]
public enum InterfaceStatus
{
    /// <summary>The device did not answer, or answered something this build does not name.</summary>
    Unknown,

    /// <summary>Up — carrying, or configured to carry, traffic.</summary>
    Up,

    /// <summary>Down.</summary>
    Down,

    /// <summary>Under test. No operational traffic passes.</summary>
    Testing,

    /// <summary>Waiting for an external event — a dial-up or a dormant tunnel.</summary>
    Dormant,

    /// <summary>The hardware the interface describes is absent.</summary>
    NotPresent,

    /// <summary>Down because the interface it runs over is down.</summary>
    LowerLayerDown
}
