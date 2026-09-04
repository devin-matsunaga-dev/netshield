using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NetShield.Inventory.Devices;

/// <summary>Maps <see cref="Device"/> to <c>devices</c>.</summary>
internal sealed class DeviceConfiguration : IEntityTypeConfiguration<Device>
{
    internal const string TableName = "devices";

    /// <summary>The index carrying the one uniqueness guarantee this package makes.</summary>
    internal const string PrimaryIpIndexName = "ix_devices_primary_ip_address_live";

    public void Configure(EntityTypeBuilder<Device> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName);

        builder.HasKey(device => device.Id);

        builder.Property(device => device.Hostname).HasMaxLength(255).IsRequired();

        // `inet`, not text. The database then refuses a value that is not an address, orders by
        // address rather than lexically, and normalises the notation on the way in — so 10.0.0.1
        // and 10.000.000.001 cannot both be stored and both claim to be free.
        builder.Property(device => device.PrimaryIpAddress).HasColumnType("inet").IsRequired();

        builder.Property(device => device.Model).HasMaxLength(128);
        builder.Property(device => device.OsVersion).HasMaxLength(128);
        builder.Property(device => device.SerialNumber).HasMaxLength(128);
        builder.Property(device => device.Site).HasMaxLength(128);
        builder.Property(device => device.Owner).HasMaxLength(128);
        builder.Property(device => device.Notes).HasMaxLength(4000);

        // Stored as the member's name rather than its ordinal, so that adding a vendor, a role or
        // a tier in a later package cannot silently renumber the rows already written.
        builder.Property(device => device.Vendor).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(device => device.Role).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(device => device.Criticality).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(device => device.Environment).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(device => device.State).HasConversion<string>().HasMaxLength(16).IsRequired();

        // text[], so a tag filter is an index-able containment test rather than a LIKE over a
        // delimited string that would match a tag inside another tag.
        builder.Property(device => device.Tags)
            .HasColumnType("text[]")
            .IsRequired();

        builder.Property(device => device.CreatedAt).IsRequired();
        builder.Property(device => device.UpdatedAt).IsRequired();

        // The unique index on primary_ip_address is created by raw SQL in the migration, not
        // declared here. EF refuses to index a property whose CLR type is not comparable, and
        // IPAddress is not — so the choice is between giving up `inet` and declaring the index
        // where EF cannot see it. `inet` is worth keeping: it validates the value, normalises the
        // notation so 10.0.0.1 and 10.000.000.001 cannot both be stored and both look free, and
        // it is what any later containment query would need. The guarantee lives in the database
        // either way; only the declaration moves.

        // Not unique. A hostname is a description, and DHCP naming, reused defaults, split DNS
        // and cloned systems all produce real duplicates that discovery has to be able to record.
        builder.HasIndex(device => device.Hostname).HasDatabaseName("ix_devices_hostname");

        // The list's default sort, and the one every keyset page walks.
        builder.HasIndex(device => new { device.CreatedAt, device.Id })
            .HasDatabaseName("ix_devices_created_at_id");

        builder.HasIndex(device => device.DeletedAt).HasDatabaseName("ix_devices_deleted_at");
    }
}
