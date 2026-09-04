namespace NetShield.Contracts.Inventory;

/// <summary>
/// How much it matters when this device fails. A manual attribute (SPEC.md §2), and one half of
/// the vulnerability prioritisation score in Phase 7.
/// </summary>
public enum CriticalityTier
{
    /// <summary>Failure is noticed but absorbed.</summary>
    Low,

    /// <summary>Failure degrades a service.</summary>
    Medium,

    /// <summary>Failure takes a service down.</summary>
    High,

    /// <summary>Failure takes the estate down.</summary>
    Critical
}
