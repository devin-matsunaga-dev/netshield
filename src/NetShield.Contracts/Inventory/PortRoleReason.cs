using System.Text.Json.Serialization;

namespace NetShield.Contracts.Inventory;

/// <summary>
/// Why a port was classified as it was, in the order the rule considers them.
/// </summary>
/// <remarks>
/// A <see cref="PortRole"/> on its own is a verdict with nothing behind it, and the address-count
/// reading in particular is a threshold somebody chose rather than something the network said. An
/// operator who thinks a port is classified wrongly can act on <see cref="LearnedAddressCount"/>
/// — it names a configured number — and cannot act on "uplink".
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<PortRoleReason>))]
public enum PortRoleReason
{
    /// <summary>Nothing was observed on the port at all.</summary>
    NoEvidence,

    /// <summary>The far end is a device NetShield monitors. The strongest reading there is.</summary>
    ManagedDevice,

    /// <summary>The far end is unmanaged and advertises itself as a bridge or a router.</summary>
    InfrastructureNeighbor,

    /// <summary>
    /// The port had learned at least as many addresses as the configured uplink threshold. A
    /// classification threshold, not a protocol truth.
    /// </summary>
    LearnedAddressCount,

    /// <summary>Endpoints were observed on the port and nothing said it was infrastructure.</summary>
    Endpoints
}
