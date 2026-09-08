using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NetShield.Inventory.Clients;

/// <summary>Maps <see cref="ClientPortBinding"/> to <c>client_port_bindings</c>.</summary>
internal sealed class ClientPortBindingConfiguration : IEntityTypeConfiguration<ClientPortBinding>
{
    internal const string TableName = "client_port_bindings";

    /// <summary>The index a client's own port history walks.</summary>
    internal const string ClientIndexName = "ix_client_port_bindings_client_id_from";

    /// <summary>The index a device's own list of attached clients reads.</summary>
    internal const string DeviceIndexName = "ix_client_port_bindings_device_id_if_index";

    /// <summary>
    /// The partial unique index: one open binding per client per device.
    /// </summary>
    /// <remarks>
    /// Per device rather than per client, deliberately. A MAC is learned by every switch on the
    /// path to it, so a client legitimately has one open binding on its access switch and one on
    /// every switch above it. A per-client rule would have each walk close another switch's
    /// correct observation, and the client would appear to move on every scan.
    /// </remarks>
    internal const string OpenBindingIndexName = "ix_client_port_bindings_open";

    public void Configure(EntityTypeBuilder<ClientPortBinding> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName);

        builder.HasKey(binding => binding.Id);

        builder.Property(binding => binding.ClientId).IsRequired();
        builder.Property(binding => binding.DeviceId).IsRequired();
        builder.Property(binding => binding.IfIndex).IsRequired();

        builder.Property(binding => binding.Source)
            .HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.Property(binding => binding.ObservedFrom).IsRequired();
        builder.Property(binding => binding.LastSeenAt).IsRequired();
        builder.Property(binding => binding.CreatedAt).IsRequired();
        builder.Property(binding => binding.UpdatedAt).IsRequired();

        builder.HasIndex(binding => new { binding.ClientId, binding.ObservedFrom })
            .HasDatabaseName(ClientIndexName);

        builder.HasIndex(binding => new { binding.DeviceId, binding.IfIndex })
            .HasDatabaseName(DeviceIndexName);

        // Every column here is comparable, so unlike the address table this partial unique index
        // can be declared where EF can see it.
        builder.HasIndex(binding => new { binding.ClientId, binding.DeviceId })
            .IsUnique()
            .HasFilter("observed_to IS NULL")
            .HasDatabaseName(OpenBindingIndexName);
    }
}
