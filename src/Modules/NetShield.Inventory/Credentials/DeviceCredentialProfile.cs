namespace NetShield.Inventory.Credentials;

/// <summary>
/// One assignment of a credential profile to a device.
/// </summary>
/// <remarks>
/// The many-to-many WP-1.2 asks for, as its own row rather than as a bare pair, because
/// CONVENTIONS.md §3 gives every table an id and its timestamps — and because when a credential
/// was assigned to a device is a fact an investigation will want.
/// </remarks>
internal sealed class DeviceCredentialProfile
{
    /// <summary>UUID v7.</summary>
    public Guid Id { get; init; }

    /// <summary>The device.</summary>
    public Guid DeviceId { get; init; }

    /// <summary>The profile it may be reached with.</summary>
    public Guid CredentialProfileId { get; init; }

    /// <summary>When the assignment was made. UTC.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the row last changed. UTC.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
