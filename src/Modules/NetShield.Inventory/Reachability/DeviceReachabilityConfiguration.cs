using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using NetShield.Inventory.Collector;

namespace NetShield.Inventory.Reachability;

/// <summary>Maps <see cref="DeviceReachability"/> to <c>device_reachability</c>.</summary>
internal sealed class DeviceReachabilityConfiguration : IEntityTypeConfiguration<DeviceReachability>
{
    internal const string TableName = "device_reachability";

    /// <summary>The index the scheduler finds due devices through.</summary>
    internal const string NextProbeIndexName = "ix_device_reachability_next_probe_at";

    /// <summary>The index carrying the one-row-per-device guarantee.</summary>
    internal const string DeviceIndexName = "ix_device_reachability_device_id";

    public void Configure(EntityTypeBuilder<DeviceReachability> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName);

        builder.HasKey(reachability => reachability.Id);

        // Stored as the member's name rather than its ordinal, so that adding a state in a later
        // package cannot silently renumber the rows already written.
        builder.Property(reachability => reachability.PendingState)
            .HasConversion<string>().HasMaxLength(16).IsRequired();

        builder.Property(reachability => reachability.PendingObservations).IsRequired();
        builder.Property(reachability => reachability.NextProbeAt).IsRequired();
        builder.Property(reachability => reachability.CreatedAt).IsRequired();
        builder.Property(reachability => reachability.UpdatedAt).IsRequired();

        // The same ceiling the collector's own failure detail has, because this is where one of
        // those ends up: it arrives on the job row already redacted and truncated.
        builder.Property(reachability => reachability.LastError)
            .HasMaxLength(CollectorLimits.DetailLength);

        // Unique, not a foreign key. `devices` soft-deletes (WP-1.1), so a cascade would erase
        // what was known about a device that was removed and a restrict would block the removal;
        // the scheduler already reads only live devices, and a row orphaned by a delete is one
        // that will simply never be scheduled again.
        builder.HasIndex(reachability => reachability.DeviceId)
            .IsUnique()
            .HasDatabaseName(DeviceIndexName);

        // The scan's only query: which devices have fallen due.
        builder.HasIndex(reachability => reachability.NextProbeAt)
            .HasDatabaseName(NextProbeIndexName);
    }
}
