using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NetShield.Inventory.Clients;

/// <summary>Maps <see cref="Client"/> to <c>clients</c>.</summary>
internal sealed class ClientConfiguration : IEntityTypeConfiguration<Client>
{
    internal const string TableName = "clients";

    /// <summary>The index carrying the one-row-per-hardware-address guarantee.</summary>
    internal const string MacIndexName = "ix_clients_mac_address";

    /// <summary>The index the list's default ordering walks.</summary>
    internal const string LastSeenIndexName = "ix_clients_last_seen_at_id";

    /// <summary>The index the OUI filter reads.</summary>
    internal const string OuiIndexName = "ix_clients_oui";

    public void Configure(EntityTypeBuilder<Client> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName);

        builder.HasKey(client => client.Id);

        // Fixed width, because every value that reaches the column has been through
        // MacAddress.Normalize and is exactly this long. A row that is not is a bug upstream.
        builder.Property(client => client.MacAddress)
            .HasMaxLength(MacAddress.Length).IsRequired();

        builder.Property(client => client.Oui).HasMaxLength(MacAddress.OuiLength).IsRequired();

        builder.Property(client => client.LocallyAdministered).IsRequired();

        builder.Property(client => client.Hostname).HasMaxLength(ClientLimits.HostnameLength);

        builder.Property(client => client.FirstSeenAt).IsRequired();
        builder.Property(client => client.LastSeenAt).IsRequired();
        builder.Property(client => client.CreatedAt).IsRequired();
        builder.Property(client => client.UpdatedAt).IsRequired();

        // The identity. Unique and not conditional: a client is not soft-deleted, so there is no
        // second state a duplicate could hide in.
        builder.HasIndex(client => client.MacAddress)
            .IsUnique()
            .HasDatabaseName(MacIndexName);

        // The list's default sort — most recently seen first — and the one every keyset page
        // walks. The id is in the index because two clients seen in the same walk share a
        // timestamp exactly.
        builder.HasIndex(client => new { client.LastSeenAt, client.Id })
            .HasDatabaseName(LastSeenIndexName);

        builder.HasIndex(client => client.Oui).HasDatabaseName(OuiIndexName);
    }
}
