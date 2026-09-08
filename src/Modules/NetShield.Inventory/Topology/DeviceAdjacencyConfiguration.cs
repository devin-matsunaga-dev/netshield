using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NetShield.Inventory.Topology;

/// <summary>Maps <see cref="DeviceAdjacency"/> to <c>device_adjacencies</c>.</summary>
internal sealed class DeviceAdjacencyConfiguration : IEntityTypeConfiguration<DeviceAdjacency>
{
    internal const string TableName = "device_adjacencies";

    /// <summary>
    /// One live edge per <c>A</c> endpoint and far end.
    /// </summary>
    /// <remarks>
    /// The port is part of it because two links between one pair of switches are two cables — an
    /// aggregate is not one edge — and the far end is part of it because a hub can put two
    /// neighbours on one port. It is what makes both devices' walks converge on one row instead
    /// of racing to write two.
    /// </remarks>
    internal const string OpenEdgeIndexName = "ix_device_adjacencies_open";

    /// <summary>The index a device's own edge list reads.</summary>
    internal const string ADeviceIndexName = "ix_device_adjacencies_a_device_id_live";

    /// <summary>The same, from the other end.</summary>
    internal const string BDeviceIndexName = "ix_device_adjacencies_b_device_id_live";

    public void Configure(EntityTypeBuilder<DeviceAdjacency> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName);

        builder.HasKey(edge => edge.Id);

        builder.Property(edge => edge.ADeviceId).IsRequired();
        builder.Property(edge => edge.AIfIndex).IsRequired();
        builder.Property(edge => edge.AInterfaceName).HasMaxLength(TopologyLimits.NameLength);
        builder.Property(edge => edge.BInterfaceName).HasMaxLength(TopologyLimits.NameLength);

        builder.Property(edge => edge.BChassisId)
            .HasMaxLength(TopologyLimits.IdentifierLength).IsRequired();

        builder.Property(edge => edge.BChassisIdKind)
            .HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.Property(edge => edge.BPortId).HasMaxLength(TopologyLimits.PortIdLength);
        builder.Property(edge => edge.BSystemName).HasMaxLength(TopologyLimits.NameLength);

        builder.Property(edge => edge.AdjacencyKey)
            .HasMaxLength(TopologyLimits.KeyLength).IsRequired();

        // text[], the shape devices.tags already uses: a containment test in the database, and
        // readable in psql without a lookup table.
        builder.Property(edge => edge.SourcesA)
            .HasColumnType("text[]")
            .IsRequired();

        builder.Property(edge => edge.SourcesB)
            .HasColumnType("text[]")
            .IsRequired();

        builder.Property(edge => edge.Confidence)
            .HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.Property(edge => edge.ObservedFromA).IsRequired();
        builder.Property(edge => edge.ObservedFromB).IsRequired();
        builder.Property(edge => edge.FirstDiscoveredAt).IsRequired();
        builder.Property(edge => edge.LastSeenAt).IsRequired();
        builder.Property(edge => edge.CreatedAt).IsRequired();
        builder.Property(edge => edge.UpdatedAt).IsRequired();

        builder.HasIndex(edge => new { edge.ADeviceId, edge.WithdrawnAt })
            .HasDatabaseName(ADeviceIndexName);

        builder.HasIndex(edge => new { edge.BDeviceId, edge.WithdrawnAt })
            .HasDatabaseName(BDeviceIndexName);

        builder.HasIndex(edge => new { edge.ADeviceId, edge.AIfIndex, edge.AdjacencyKey })
            .HasFilter("withdrawn_at IS NULL")
            .IsUnique()
            .HasDatabaseName(OpenEdgeIndexName);
    }
}
