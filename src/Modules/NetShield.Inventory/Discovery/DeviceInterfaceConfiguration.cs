using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NetShield.Inventory.Discovery;

/// <summary>Maps <see cref="DeviceInterface"/> to <c>device_interfaces</c>.</summary>
internal sealed class DeviceInterfaceConfiguration : IEntityTypeConfiguration<DeviceInterface>
{
    internal const string TableName = "device_interfaces";

    /// <summary>The index carrying the one-row-per-ifIndex-per-device guarantee.</summary>
    internal const string DeviceIfIndexName = "ix_device_interfaces_device_id_if_index";

    public void Configure(EntityTypeBuilder<DeviceInterface> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName);

        builder.HasKey(item => item.Id);

        builder.Property(item => item.IfIndex).IsRequired();
        builder.Property(item => item.FirstSeenAt).IsRequired();
        builder.Property(item => item.LastSeenAt).IsRequired();
        builder.Property(item => item.CreatedAt).IsRequired();
        builder.Property(item => item.UpdatedAt).IsRequired();

        builder.Property(item => item.Name).HasMaxLength(DiscoveryLimits.InterfaceTextLength);
        builder.Property(item => item.Description).HasMaxLength(DiscoveryLimits.InterfaceTextLength);
        builder.Property(item => item.Alias).HasMaxLength(DiscoveryLimits.InterfaceTextLength);
        builder.Property(item => item.PhysicalAddress)
            .HasMaxLength(DiscoveryLimits.PhysicalAddressLength);

        // The identity of an interface within a device, and the key a walk reconciles on.
        // Unique rather than a foreign key, for the reason every other index in this module is:
        // devices soft-delete and a cascade would decide the retention question by accident.
        builder.HasIndex(item => new { item.DeviceId, item.IfIndex })
            .IsUnique()
            .HasDatabaseName(DeviceIfIndexName);
    }
}
