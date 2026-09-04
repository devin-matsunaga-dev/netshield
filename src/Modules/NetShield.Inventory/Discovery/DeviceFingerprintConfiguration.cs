using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using NetShield.Inventory.Collector;

namespace NetShield.Inventory.Discovery;

/// <summary>Maps <see cref="DeviceFingerprint"/> to <c>device_fingerprints</c>.</summary>
internal sealed class DeviceFingerprintConfiguration : IEntityTypeConfiguration<DeviceFingerprint>
{
    internal const string TableName = "device_fingerprints";

    /// <summary>The index carrying the one-row-per-device guarantee.</summary>
    internal const string DeviceIndexName = "ix_device_fingerprints_device_id";

    public void Configure(EntityTypeBuilder<DeviceFingerprint> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName);

        builder.HasKey(fingerprint => fingerprint.Id);

        // Stored as the member's name rather than its ordinal, so that adding a vendor in a
        // later package cannot silently renumber the rows already written — the same reason
        // devices.vendor is stored this way (WP-1.1).
        builder.Property(fingerprint => fingerprint.Vendor)
            .HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.Property(fingerprint => fingerprint.ReducedCapability).IsRequired();
        builder.Property(fingerprint => fingerprint.InterfaceCount).IsRequired();
        builder.Property(fingerprint => fingerprint.InterfacesTruncated).IsRequired();
        builder.Property(fingerprint => fingerprint.CreatedAt).IsRequired();
        builder.Property(fingerprint => fingerprint.UpdatedAt).IsRequired();

        builder.Property(fingerprint => fingerprint.SysObjectId)
            .HasMaxLength(DiscoveryLimits.ObjectIdLength);
        builder.Property(fingerprint => fingerprint.SysDescr)
            .HasMaxLength(DiscoveryLimits.DescriptionLength);
        builder.Property(fingerprint => fingerprint.SysName)
            .HasMaxLength(DiscoveryLimits.NameLength);
        builder.Property(fingerprint => fingerprint.SysContact)
            .HasMaxLength(DiscoveryLimits.NameLength);
        builder.Property(fingerprint => fingerprint.SysLocation)
            .HasMaxLength(DiscoveryLimits.NameLength);
        builder.Property(fingerprint => fingerprint.Model)
            .HasMaxLength(DiscoveryLimits.NameLength);
        builder.Property(fingerprint => fingerprint.OsVersion)
            .HasMaxLength(DiscoveryLimits.NameLength);
        builder.Property(fingerprint => fingerprint.SerialNumber)
            .HasMaxLength(DiscoveryLimits.NameLength);

        // The same ceiling the collector's own failure detail has, because this is where one of
        // those ends up: it arrives on the job row already redacted and truncated.
        builder.Property(fingerprint => fingerprint.LastError)
            .HasMaxLength(CollectorLimits.DetailLength);

        // text[], the shape devices.tags already uses. Four member names at most, so a column
        // rather than a table: nothing joins on it and nothing queries by it.
        builder.Property(fingerprint => fingerprint.OverriddenFields)
            .HasColumnType("text[]")
            .IsRequired();

        // Unique, not a foreign key. `devices` soft-deletes, so a cascade would erase what was
        // known about a device that was removed and a restrict would block the removal — the
        // same reasoning device_reachability uses (WP-1.4).
        builder.HasIndex(fingerprint => fingerprint.DeviceId)
            .IsUnique()
            .HasDatabaseName(DeviceIndexName);
    }
}
