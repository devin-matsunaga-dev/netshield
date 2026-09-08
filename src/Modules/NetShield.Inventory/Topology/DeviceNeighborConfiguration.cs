using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NetShield.Inventory.Topology;

/// <summary>Maps <see cref="DeviceNeighbor"/> to <c>device_neighbors</c>.</summary>
internal sealed class DeviceNeighborConfiguration : IEntityTypeConfiguration<DeviceNeighbor>
{
    internal const string TableName = "device_neighbors";

    /// <summary>
    /// The invariant the applier rests on: one live observation per device, source, port and far
    /// end. A walk that failed to withdraw the previous account of a port then fails its insert
    /// rather than leaving one port with two current neighbours from one protocol.
    /// </summary>
    internal const string OpenObservationIndexName = "ix_device_neighbors_open";

    /// <summary>The index the reconciler reads: one device's live observations.</summary>
    internal const string DeviceIndexName = "ix_device_neighbors_device_id_live";

    /// <summary>The index that fetches the evidence behind an edge.</summary>
    internal const string AdjacencyIndexName = "ix_device_neighbors_adjacency_id";

    public void Configure(EntityTypeBuilder<DeviceNeighbor> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName);

        builder.HasKey(row => row.Id);

        builder.Property(row => row.DeviceId).IsRequired();

        // Stored as the member's name, so a source added later cannot renumber rows already
        // written — the rule WP-0.4 settled for every enum at rest.
        builder.Property(row => row.Source)
            .HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.Property(row => row.LocalIfIndex).IsRequired();
        builder.Property(row => row.LocalInterfaceName).HasMaxLength(TopologyLimits.NameLength);

        builder.Property(row => row.RemoteChassisId)
            .HasMaxLength(TopologyLimits.IdentifierLength).IsRequired();

        builder.Property(row => row.RemoteChassisIdKind)
            .HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.Property(row => row.RemotePortId).HasMaxLength(TopologyLimits.PortIdLength);

        builder.Property(row => row.RemotePortIdKind)
            .HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.Property(row => row.RemotePortDescription).HasMaxLength(TopologyLimits.NameLength);
        builder.Property(row => row.RemoteSystemName).HasMaxLength(TopologyLimits.NameLength);
        builder.Property(row => row.RemoteSystemDescription)
            .HasMaxLength(TopologyLimits.DescriptionLength);
        builder.Property(row => row.RemotePlatform).HasMaxLength(TopologyLimits.NameLength);
        builder.Property(row => row.RemoteInterfaceName).HasMaxLength(TopologyLimits.NameLength);

        // `inet`, for the reason devices.primary_ip_address is: the database refuses a value that
        // is not an address and normalises the notation, so one management address cannot be
        // stored under two spellings and resolve to a device only half the time.
        builder.Property(row => row.RemoteManagementAddress).HasColumnType("inet");

        builder.Property(row => row.RemoteKey)
            .HasMaxLength(TopologyLimits.KeyLength).IsRequired();

        builder.Property(row => row.AdjacencyKey)
            .HasMaxLength(TopologyLimits.KeyLength).IsRequired();

        builder.Property(row => row.FirstDiscoveredAt).IsRequired();
        builder.Property(row => row.LastSeenAt).IsRequired();
        builder.Property(row => row.CreatedAt).IsRequired();
        builder.Property(row => row.UpdatedAt).IsRequired();

        // No foreign key to `devices`, for the reason `client_ip_bindings` has none: devices
        // soft-delete, so a cascade would erase the record of what a removed device observed and
        // a restrict would block the removal. The reads join on live devices.
        builder.HasIndex(row => new { row.DeviceId, row.WithdrawnAt })
            .HasDatabaseName(DeviceIndexName);

        builder.HasIndex(row => row.AdjacencyId)
            .HasDatabaseName(AdjacencyIndexName);

        builder.HasIndex(row => row.RemoteDeviceId)
            .HasDatabaseName("ix_device_neighbors_remote_device_id");

        builder.HasIndex(row => new
        {
            row.DeviceId,
            row.Source,
            row.LocalIfIndex,
            row.RemoteKey
        })
            .HasFilter("withdrawn_at IS NULL")
            .IsUnique()
            .HasDatabaseName(OpenObservationIndexName);
    }
}
