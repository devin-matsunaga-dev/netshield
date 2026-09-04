namespace NetShield.Inventory.Discovery;

/// <summary>
/// One interface on one device, as the last SNMP walk found it.
/// </summary>
/// <remarks>
/// <para>
/// Derived data: every column is reconstructible by walking the device again, and nothing an
/// operator types reaches this table. It is therefore not soft-deleted — CONVENTIONS.md §3 puts
/// soft delete on the inventory an operator maintains, and an interface that has genuinely gone
/// from a device is noise in every list it would otherwise stay in.
/// </para>
/// <para>
/// <strong>Absence is only evidence on a complete walk.</strong> A walk that hit the interface
/// ceiling read part of the table, so an interface it did not mention may simply not have been
/// reached. Rows are removed only when the walk that replaced them saw the whole table.
/// </para>
/// <para>
/// <see cref="FirstSeenAt"/> survives across walks. "This port appeared last Tuesday" is a fact
/// an operator wants and a walk cannot reconstruct, so an interface that is still there keeps
/// the timestamp of the walk that first found it.
/// </para>
/// </remarks>
internal sealed class DeviceInterface
{
    /// <summary>UUID v7.</summary>
    public Guid Id { get; init; }

    /// <summary>The device this interface belongs to.</summary>
    public Guid DeviceId { get; init; }

    /// <summary>``ifIndex``. Unique within the device, and how a walk finds the row to update.</summary>
    public int IfIndex { get; init; }

    /// <summary>``ifName``, absent on a device that implements no ``ifXTable``.</summary>
    public string? Name { get; set; }

    /// <summary>``ifDescr``.</summary>
    public string? Description { get; set; }

    /// <summary>``ifAlias`` — the description an operator configured on the device itself.</summary>
    public string? Alias { get; set; }

    /// <summary>``ifType``, the IANA interface type.</summary>
    public int? InterfaceType { get; set; }

    /// <summary>``ifMtu``.</summary>
    public int? Mtu { get; set; }

    /// <summary>
    /// The interface's speed in bits per second, or nothing where the device could not express
    /// one — a saturated 32-bit ``ifSpeed`` with no ``ifHighSpeed`` beside it is the absence of a
    /// measurement rather than a measurement of 4.29 Gbit/s.
    /// </summary>
    public long? SpeedBitsPerSecond { get; set; }

    /// <summary>``ifPhysAddress``, as colon-separated hex.</summary>
    public string? PhysicalAddress { get; set; }

    /// <summary>``ifAdminStatus``.</summary>
    public int? AdminStatus { get; set; }

    /// <summary>``ifOperStatus``.</summary>
    public int? OperStatus { get; set; }

    /// <summary>When a walk first found this interface. UTC, and never rewritten.</summary>
    public DateTimeOffset FirstSeenAt { get; init; }

    /// <summary>When a walk last found it. UTC.</summary>
    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>When the row was created. UTC.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the row last changed. UTC.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
