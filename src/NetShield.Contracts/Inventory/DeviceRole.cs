namespace NetShield.Contracts.Inventory;

/// <summary>What a device is for. Drives the golden template config drift is measured against.</summary>
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
