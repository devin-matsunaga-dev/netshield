using NetShield.Contracts.Inventory;

namespace NetShield.Inventory.Discovery;

/// <summary>
/// What the last SNMP walk established about a device, and which of those facts an operator has
/// since overruled.
/// </summary>
/// <remarks>
/// <para>
/// A table of its own rather than columns on <c>devices</c>, for the reason
/// <c>device_reachability</c> is one: <c>devices</c> is the inventory an operator maintains, and
/// this is what a machine observed. The four facts an operator does maintain — vendor, model, OS
/// version and serial — stay on the device, because that is where every list, filter and report
/// reads them from; what is here is the walk's own copy of them, and it is what makes the
/// difference between "discovered" and "overridden" answerable.
/// </para>
/// <para>
/// <strong>The override rule.</strong> WP-1.1 gave the device no provenance column and WP-1.5
/// was not told to add one, so provenance is derived rather than declared: the walk compares
/// what the device says now against what the <em>previous</em> walk discovered, and a difference
/// means somebody changed it in between. That field is then left alone and named in
/// <see cref="OverriddenFields"/>. A device that has never been walked has no baseline to have
/// diverged from, so the first walk owns everything — which is right: fingerprinting exists to
/// correct a guess an operator typed in.
/// </para>
/// <para>
/// One row per device, created by the first walk that reaches it.
/// </para>
/// </remarks>
internal sealed class DeviceFingerprint
{
    /// <summary>UUID v7, so the primary key is also the order rows were created in.</summary>
    public Guid Id { get; init; }

    /// <summary>The device this is about. Unique — a device has one fingerprint row.</summary>
    public Guid DeviceId { get; init; }

    /// <summary>The vendor the last walk resolved, whatever the device row now says.</summary>
    public DeviceVendor Vendor { get; set; }

    /// <summary>
    /// Whether the last walk landed on the generic-SNMP fallback.
    /// </summary>
    /// <remarks>
    /// SPEC.md §4 requires a reduced feature set to be clearly labelled. This is the recorded
    /// fact that label is drawn from — an observation made at walk time, not something a screen
    /// infers from the vendor name, so that a device pinned to a vendor by an operator does not
    /// thereby claim CLI features nothing has demonstrated.
    /// </remarks>
    public bool ReducedCapability { get; set; }

    /// <summary>``sysObjectID`` — the vendor's own identifier for the platform.</summary>
    public string? SysObjectId { get; set; }

    /// <summary>``sysDescr``, as the device wrote it.</summary>
    public string? SysDescr { get; set; }

    /// <summary>``sysName`` — the device's own idea of its name, which need not be its hostname.</summary>
    public string? SysName { get; set; }

    /// <summary>``sysContact``.</summary>
    public string? SysContact { get; set; }

    /// <summary>``sysLocation``.</summary>
    public string? SysLocation { get; set; }

    /// <summary>
    /// ``sysUpTime`` in seconds at <see cref="LastWalkAt"/>, or nothing if the device did not
    /// answer it. A 32-bit counter that wraps after about 497 days: this is what the agent said,
    /// not a boot time derived from it.
    /// </summary>
    public double? UptimeSeconds { get; set; }

    /// <summary>The model the last walk discovered. May differ from the device's, if pinned.</summary>
    public string? Model { get; set; }

    /// <summary>The OS version the last walk discovered.</summary>
    public string? OsVersion { get; set; }

    /// <summary>The serial the last walk discovered.</summary>
    public string? SerialNumber { get; set; }

    /// <summary>How many interfaces the last walk found, before any truncation.</summary>
    public int InterfaceCount { get; set; }

    /// <summary>
    /// Whether the last walk read fewer interfaces than the device has.
    /// </summary>
    /// <remarks>
    /// It decides whether an interface missing from a walk is evidence that it is gone. On a
    /// truncated walk it is not, and no interface row is removed.
    /// </remarks>
    public bool InterfacesTruncated { get; set; }

    /// <summary>
    /// Which of <c>vendor</c>, <c>model</c>, <c>osVersion</c> and <c>serialNumber</c> an operator
    /// has set to something other than what the previous walk discovered.
    /// </summary>
    /// <remarks>
    /// Recomputed on every walk, and stored rather than derived at read time so that the rule is
    /// visible in the row a person is looking at. Empty is the ordinary case.
    /// </remarks>
    public IReadOnlyList<string> OverriddenFields { get; set; } = [];

    /// <summary>When a walk last reached the device. UTC.</summary>
    public DateTimeOffset? LastWalkAt { get; set; }

    /// <summary>
    /// The job whose result was last applied to this row.
    /// </summary>
    /// <remarks>
    /// Outbox delivery is at-least-once, and this row plus the interface rows beside it are what
    /// one delivery rewrites. A result whose job has already been applied is dropped, the same
    /// guard <c>device_reachability</c> carries and for the same reason.
    /// </remarks>
    public Guid? LastAppliedJobId { get; set; }

    /// <summary>
    /// Why the last walk could not be performed, or <see langword="null"/> if the last one ran.
    /// </summary>
    /// <remarks>
    /// The collector's problem, not the device's. It is recorded without touching anything else
    /// on this row: a failed walk must not be able to erase what a successful one established.
    /// </remarks>
    public string? LastError { get; set; }

    /// <summary>When the row was created. UTC.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the row last changed. UTC.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
