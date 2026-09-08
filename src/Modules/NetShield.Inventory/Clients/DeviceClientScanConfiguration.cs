using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using NetShield.Inventory.Collector;

namespace NetShield.Inventory.Clients;

/// <summary>Maps <see cref="DeviceClientScan"/> to <c>device_client_scans</c>.</summary>
internal sealed class DeviceClientScanConfiguration : IEntityTypeConfiguration<DeviceClientScan>
{
    internal const string TableName = "device_client_scans";

    /// <summary>The index the scheduler finds due devices through.</summary>
    internal const string NextWalkIndexName = "ix_device_client_scans_next_walk_at";

    /// <summary>The index carrying the one-row-per-device guarantee.</summary>
    internal const string DeviceIndexName = "ix_device_client_scans_device_id";

    public void Configure(EntityTypeBuilder<DeviceClientScan> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName);

        builder.HasKey(scan => scan.Id);

        builder.Property(scan => scan.NextWalkAt).IsRequired();
        builder.Property(scan => scan.CreatedAt).IsRequired();
        builder.Property(scan => scan.UpdatedAt).IsRequired();

        // The same ceiling the collector's own failure detail has, because this is where one of
        // those ends up: it arrives on the job row already redacted and truncated.
        builder.Property(scan => scan.LastError).HasMaxLength(CollectorLimits.DetailLength);

        // Unique, not a foreign key, for the reason device_reachability's is: devices
        // soft-delete, so a cascade would erase what was known about a removed device and a
        // restrict would block the removal.
        builder.HasIndex(scan => scan.DeviceId).IsUnique().HasDatabaseName(DeviceIndexName);

        builder.HasIndex(scan => scan.NextWalkAt).HasDatabaseName(NextWalkIndexName);
    }
}
