using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NetShield.Inventory.Topology;

/// <summary>Maps <see cref="DeviceVlan"/> to <c>device_vlans</c>.</summary>
internal sealed class DeviceVlanConfiguration : IEntityTypeConfiguration<DeviceVlan>
{
    internal const string TableName = "device_vlans";

    /// <summary>
    /// The invariant the applier rests on: one live row per device and VLAN id. A walk that
    /// failed to withdraw the previous account of a VLAN then fails its insert rather than
    /// leaving one switch carrying VLAN 20 twice, which would double every count taken over it.
    /// </summary>
    internal const string OpenVlanIndexName = "ix_device_vlans_open";

    /// <summary>The index the applier and the per-device read use: one device's live VLANs.</summary>
    internal const string DeviceIndexName = "ix_device_vlans_device_id_live";

    /// <summary>The index the estate-wide aggregation groups by.</summary>
    internal const string VlanIndexName = "ix_device_vlans_vlan_id_live";

    public void Configure(EntityTypeBuilder<DeviceVlan> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName);

        builder.HasKey(row => row.Id);

        builder.Property(row => row.DeviceId).IsRequired();
        builder.Property(row => row.VlanId).IsRequired();
        builder.Property(row => row.Name).HasMaxLength(TopologyLimits.NameLength);
        builder.Property(row => row.Source).HasMaxLength(TopologyLimits.TableNameLength);

        // `integer[]`, which is what EFCore.NamingConventions and the Npgsql provider make of an
        // int[] property without being told. The membership is always read and written whole.
        builder.Property(row => row.IfIndexes).IsRequired();
        builder.Property(row => row.UntaggedIfIndexes).IsRequired();

        builder.Property(row => row.PortCount).IsRequired();
        builder.Property(row => row.UnresolvedPortCount).IsRequired();
        builder.Property(row => row.FirstDiscoveredAt).IsRequired();
        builder.Property(row => row.LastSeenAt).IsRequired();
        builder.Property(row => row.CreatedAt).IsRequired();
        builder.Property(row => row.UpdatedAt).IsRequired();

        // No foreign key to `devices`, for the reason `device_neighbors` has none: devices
        // soft-delete, so a cascade would erase the record of what a removed device carried and a
        // restrict would block the removal. The reads join on live devices.
        builder.HasIndex(row => new { row.DeviceId, row.WithdrawnAt })
            .HasDatabaseName(DeviceIndexName);

        builder.HasIndex(row => new { row.VlanId, row.WithdrawnAt })
            .HasDatabaseName(VlanIndexName);

        builder.HasIndex(row => new { row.DeviceId, row.VlanId })
            .HasFilter("withdrawn_at IS NULL")
            .IsUnique()
            .HasDatabaseName(OpenVlanIndexName);
    }
}
