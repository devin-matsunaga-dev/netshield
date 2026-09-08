using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NetShield.Inventory.Topology;

/// <summary>Maps <see cref="DeviceTopologyScan"/> to <c>device_topology_scans</c>.</summary>
internal sealed class DeviceTopologyScanConfiguration : IEntityTypeConfiguration<DeviceTopologyScan>
{
    internal const string TableName = "device_topology_scans";

    /// <summary>One scan row per device, which the schedule and both handlers rely on.</summary>
    internal const string DeviceIndexName = "ix_device_topology_scans_device_id";

    /// <summary>The index the neighbour schedule sorts by.</summary>
    internal const string NextNeighborIndexName = "ix_device_topology_scans_next_neighbor_walk_at";

    /// <summary>The index the route schedule sorts by.</summary>
    internal const string NextRouteIndexName = "ix_device_topology_scans_next_route_walk_at";

    public void Configure(EntityTypeBuilder<DeviceTopologyScan> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName);

        builder.HasKey(scan => scan.Id);

        builder.Property(scan => scan.DeviceId).IsRequired();
        builder.Property(scan => scan.NextNeighborWalkAt).IsRequired();
        builder.Property(scan => scan.NextRouteWalkAt).IsRequired();
        builder.Property(scan => scan.RouteTable).HasMaxLength(TopologyLimits.TableNameLength);
        builder.Property(scan => scan.LastNeighborError).HasMaxLength(TopologyLimits.ErrorLength);
        builder.Property(scan => scan.LastRouteError).HasMaxLength(TopologyLimits.ErrorLength);
        builder.Property(scan => scan.CreatedAt).IsRequired();
        builder.Property(scan => scan.UpdatedAt).IsRequired();

        builder.HasIndex(scan => scan.DeviceId)
            .IsUnique()
            .HasDatabaseName(DeviceIndexName);

        builder.HasIndex(scan => scan.NextNeighborWalkAt).HasDatabaseName(NextNeighborIndexName);
        builder.HasIndex(scan => scan.NextRouteWalkAt).HasDatabaseName(NextRouteIndexName);
    }
}
