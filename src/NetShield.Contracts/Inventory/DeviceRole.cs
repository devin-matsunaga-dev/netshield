using System.Text.Json.Serialization;

namespace NetShield.Contracts.Inventory;

/// <summary>What a device is for. Drives the golden template config drift is measured against.</summary>
/// <remarks>
/// Serialised as its name rather than its ordinal, so that inserting a member cannot renumber
/// what a stored row, a generated client or a saved fixture already means (WP-0.4). The
/// attribute has to sit on the type: naming a converter in a serializer context's
/// <c>JsonSourceGenerationOptions</c> has no effect once that context is one resolver among
/// several on the host's JSON options.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<DeviceRole>))]
public enum DeviceRole
{
    /// <summary>A device whose role is known but is not one this list names.</summary>
    Other,

    /// <summary>Routes between networks.</summary>
    Router,

    /// <summary>Switches within a network, at any layer.</summary>
    Switch,

    /// <summary>Enforces a security policy between networks.</summary>
    Firewall,

    /// <summary>A wireless access point.</summary>
    AccessPoint,

    /// <summary>Distributes traffic across a pool.</summary>
    LoadBalancer,

    /// <summary>A host NetShield monitors as infrastructure rather than as a client.</summary>
    Server
}
