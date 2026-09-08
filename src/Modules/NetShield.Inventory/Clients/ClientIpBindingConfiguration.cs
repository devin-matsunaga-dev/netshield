using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NetShield.Inventory.Clients;

/// <summary>Maps <see cref="ClientIpBinding"/> to <c>client_ip_bindings</c>.</summary>
internal sealed class ClientIpBindingConfiguration : IEntityTypeConfiguration<ClientIpBinding>
{
    internal const string TableName = "client_ip_bindings";

    /// <summary>
    /// The index every resolution reads: an address, and the intervals it has had, newest first.
    /// </summary>
    internal const string AddressIndexName = "ix_client_ip_bindings_address_from";

    /// <summary>The index a client's own history walks.</summary>
    internal const string ClientIndexName = "ix_client_ip_bindings_client_id_from";

    /// <summary>
    /// The partial unique index carrying the invariant the whole package rests on: an address has
    /// at most one open binding. Created by raw SQL in the migration, because EF will not index a
    /// property whose CLR type is not comparable and <c>IPAddress</c> is not — the same
    /// arrangement <c>ix_devices_primary_ip_address_live</c> already uses.
    /// </summary>
    internal const string OpenAddressIndexName = "ix_client_ip_bindings_address_open";

    public void Configure(EntityTypeBuilder<ClientIpBinding> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName);

        builder.HasKey(binding => binding.Id);

        builder.Property(binding => binding.ClientId).IsRequired();

        // `inet`, for the reason devices.primary_ip_address is: the database refuses a value
        // that is not an address and normalises the notation, so one address cannot be stored
        // under two spellings and hold two open bindings that the unique index cannot see.
        builder.Property(binding => binding.IpAddress).HasColumnType("inet").IsRequired();

        // Stored as the member's name, so a source added later cannot renumber rows already
        // written — and two of the four members are declared before anything produces them.
        builder.Property(binding => binding.Source)
            .HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.Property(binding => binding.ObservedFrom).IsRequired();
        builder.Property(binding => binding.LastSeenAt).IsRequired();
        builder.Property(binding => binding.CreatedAt).IsRequired();
        builder.Property(binding => binding.UpdatedAt).IsRequired();

        // Not a foreign key to `devices`, for the reason `device_reachability` has none: devices
        // soft-delete, so a cascade would erase history about a device that was removed and a
        // restrict would block the removal. A binding naming a device that has gone keeps saying
        // what it observed, and the read joins on live devices only.
        builder.HasIndex(binding => binding.DeviceId)
            .HasDatabaseName("ix_client_ip_bindings_device_id");

        builder.HasIndex(binding => new { binding.ClientId, binding.ObservedFrom })
            .HasDatabaseName(ClientIndexName);

        // The resolution query's index. `ip_address` cannot be part of an EF-declared index for
        // the reason above, so this one is raw SQL in the migration too; what stays here is the
        // name, so that a reader of this file sees every index the table has.
    }
}
