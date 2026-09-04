using System.Net;

using NetShield.Contracts.Inventory;

namespace NetShield.Inventory.Devices;

/// <summary>
/// A monitored network device.
/// </summary>
/// <remarks>
/// Internal, and it stays internal: ARCHITECTURE.md §4 lets DTOs cross a module boundary and
/// nothing else. Everything outside <c>NetShield.Inventory</c> sees <c>DeviceSummary</c>,
/// <c>DeviceDetail</c>, or an integration event.
/// </remarks>
internal sealed class Device
{
    /// <summary>UUID v7, so the primary key is also the creation order (CONVENTIONS.md §3).</summary>
    public Guid Id { get; init; }

    /// <summary>
    /// The name this device is known by. Indexed, and deliberately not unique — a hostname is a
    /// description, not an identity, and duplicates are real.
    /// </summary>
    public required string Hostname { get; set; }

    /// <summary>
    /// The address NetShield reaches it on. Unique across devices that are not deleted; a
    /// soft-deleted device releases its address for reuse.
    /// </summary>
    public required IPAddress PrimaryIpAddress { get; set; }

    /// <summary>The platform, once something has identified it.</summary>
    public DeviceVendor Vendor { get; set; }

    /// <summary>The hardware model, when known.</summary>
    public string? Model { get; set; }

    /// <summary>The running software version, when known.</summary>
    public string? OsVersion { get; set; }

    /// <summary>The chassis serial, when known.</summary>
    public string? SerialNumber { get; set; }

    /// <summary>Where the device is. Free text until a site aggregate exists.</summary>
    public string? Site { get; set; }

    /// <summary>What the device is for.</summary>
    public DeviceRole Role { get; set; }

    /// <summary>How much its failure matters.</summary>
    public CriticalityTier Criticality { get; set; }

    /// <summary>Which environment it belongs to.</summary>
    public DeviceEnvironment Environment { get; set; }

    /// <summary>Who is responsible for it. Free text.</summary>
    public string? Owner { get; set; }

    /// <summary>Free-form labels, already normalised by <see cref="DeviceTags"/>.</summary>
    public IReadOnlyList<string> Tags { get; set; } = [];

    /// <summary>Anything an operator wanted to record.</summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Whether the device is answering. Nothing in this package writes anything but the default:
    /// the state machine in WP-1.4 owns every transition.
    /// </summary>
    public DeviceState State { get; set; } = DeviceState.Unknown;

    /// <summary>When the device was added. UTC.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the row last changed. UTC.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// When the device was removed, or <see langword="null"/> while it is live. Soft delete, per
    /// CONVENTIONS.md §3 — telemetry already written against this device keeps its reference.
    /// </summary>
    public DateTimeOffset? DeletedAt { get; set; }
}
