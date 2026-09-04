using System.Text.Json.Serialization;

namespace NetShield.Inventory.Discovery;

/// <summary>
/// What the collector established about a device, as it is written into
/// <c>collector_jobs.result</c>.
/// </summary>
/// <remarks>
/// <para>
/// This shape is only ever produced by a walk that reached the device. A walk that could not run
/// — no route, the wrong credential, an agent that answered nothing — reports a failed job with a
/// redacted sentence and no payload, and the result handler records that against the device
/// without touching what is already known about it. Replacing a known fingerprint with "nothing"
/// because one walk failed would lose what an earlier walk had established.
/// </para>
/// <para>
/// <see cref="Vendor"/> arrives as the <c>DeviceVendor</c> member's name, matched by string
/// against the adapter that answered on the collector side. The two lists are kept in step by
/// tests on both sides rather than by a generator: there is none between the repositories, and a
/// vendor added to one and not the other should fail a test rather than quietly resolve to
/// generic SNMP.
/// </para>
/// </remarks>
/// <param name="Walk">The discriminator, matching <see cref="SnmpWalkParameters.WalkName"/>.</param>
/// <param name="Vendor">The <c>DeviceVendor</c> member the collector resolved, by name.</param>
/// <param name="ReducedCapability">
/// Whether the device landed on the generic-SNMP fallback. SPEC.md §4 requires this to be
/// labelled in the UI, so it is recorded as a fact observed at walk time rather than left for a
/// screen to infer from the vendor name.
/// </param>
/// <param name="SysObjectId">``sysObjectID``, the vendor's own identifier for the platform.</param>
/// <param name="SysDescr">``sysDescr``, as the device wrote it.</param>
/// <param name="SysName">``sysName`` — the device's own idea of its name, not NetShield's.</param>
/// <param name="SysContact">``sysContact``.</param>
/// <param name="SysLocation">``sysLocation``.</param>
/// <param name="UptimeSeconds">
/// ``sysUpTime`` in seconds, as the agent reported it. A 32-bit counter that wraps after about
/// 497 days; nothing here reconstructs a boot time from it.
/// </param>
/// <param name="Model">The hardware model, from wherever this vendor keeps it.</param>
/// <param name="OsVersion">The running software version.</param>
/// <param name="SerialNumber">The chassis serial.</param>
/// <param name="InterfaceCount">How many interfaces the device reported, before any truncation.</param>
/// <param name="InterfacesTruncated">
/// Whether <see cref="Interfaces"/> is shorter than <see cref="InterfaceCount"/>. It decides
/// whether an interface that is absent from this walk is evidence that it is gone.
/// </param>
/// <param name="Interfaces">The interface inventory, in ``ifIndex`` order.</param>
internal sealed record SnmpWalkResult(
    [property: JsonPropertyName("walk")] string? Walk,
    [property: JsonPropertyName("vendor")] string? Vendor,
    [property: JsonPropertyName("reducedCapability")] bool ReducedCapability,
    [property: JsonPropertyName("sysObjectId")] string? SysObjectId,
    [property: JsonPropertyName("sysDescr")] string? SysDescr,
    [property: JsonPropertyName("sysName")] string? SysName,
    [property: JsonPropertyName("sysContact")] string? SysContact,
    [property: JsonPropertyName("sysLocation")] string? SysLocation,
    [property: JsonPropertyName("uptimeSeconds")] double? UptimeSeconds,
    [property: JsonPropertyName("model")] string? Model,
    [property: JsonPropertyName("osVersion")] string? OsVersion,
    [property: JsonPropertyName("serialNumber")] string? SerialNumber,
    [property: JsonPropertyName("interfaceCount")] int InterfaceCount,
    [property: JsonPropertyName("interfacesTruncated")] bool InterfacesTruncated,
    [property: JsonPropertyName("interfaces")] IReadOnlyList<SnmpWalkInterface>? Interfaces);

/// <summary>One interface, joined from ``ifTable`` and ``ifXTable`` on the collector side.</summary>
/// <param name="Index">``ifIndex``, and the identity of the interface within its device.</param>
/// <param name="Name">``ifName``, absent on a device that implements no ``ifXTable``.</param>
/// <param name="Description">``ifDescr``.</param>
/// <param name="Alias">``ifAlias`` — the description an operator configured.</param>
/// <param name="InterfaceType">``ifType``, the IANA interface type.</param>
/// <param name="Mtu">``ifMtu``.</param>
/// <param name="SpeedBitsPerSecond">
/// ``ifHighSpeed`` where the device offers one, ``ifSpeed`` otherwise, and nothing at all where
/// the 32-bit gauge saturated with no wider column beside it.
/// </param>
/// <param name="PhysicalAddress">``ifPhysAddress``, as colon-separated hex.</param>
/// <param name="AdminStatus">``ifAdminStatus``.</param>
/// <param name="OperStatus">``ifOperStatus``.</param>
internal sealed record SnmpWalkInterface(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("alias")] string? Alias,
    [property: JsonPropertyName("interfaceType")] int? InterfaceType,
    [property: JsonPropertyName("mtu")] int? Mtu,
    [property: JsonPropertyName("speedBitsPerSecond")] long? SpeedBitsPerSecond,
    [property: JsonPropertyName("physicalAddress")] string? PhysicalAddress,
    [property: JsonPropertyName("adminStatus")] int? AdminStatus,
    [property: JsonPropertyName("operStatus")] int? OperStatus);
