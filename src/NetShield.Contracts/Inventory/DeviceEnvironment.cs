namespace NetShield.Contracts.Inventory;

/// <summary>Which environment a device belongs to. A manual attribute (SPEC.md §2).</summary>
public enum DeviceEnvironment
{
    /// <summary>Carries live traffic.</summary>
    Production,

    /// <summary>Pre-production, shaped like production.</summary>
    Staging,

    /// <summary>Where changes are built.</summary>
    Development,

    /// <summary>A test bench. Not expected to be stable.</summary>
    Lab
}
