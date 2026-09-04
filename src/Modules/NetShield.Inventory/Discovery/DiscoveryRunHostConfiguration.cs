using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NetShield.Inventory.Discovery;

/// <summary>Maps <see cref="DiscoveryRunHost"/> to <c>discovery_run_hosts</c>.</summary>
internal sealed class DiscoveryRunHostConfiguration : IEntityTypeConfiguration<DiscoveryRunHost>
{
    internal const string TableName = "discovery_run_hosts";

    public void Configure(EntityTypeBuilder<DiscoveryRunHost> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName);

        builder.HasKey(host => host.Id);

        builder.Property(host => host.RunId).IsRequired();

        // `inet`, for the reason devices.primary_ip_address is: the database refuses a value
        // that is not an address and normalises the notation, so one address cannot appear in
        // two spellings in a run's history.
        builder.Property(host => host.Address).HasColumnType("inet").IsRequired();

        builder.Property(host => host.Outcome).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(host => host.ObservedAt).IsRequired();
        builder.Property(host => host.CreatedAt).IsRequired();
        builder.Property(host => host.UpdatedAt).IsRequired();

        // The per-run list, and the keyset page that walks it.
        builder.HasIndex(host => new { host.RunId, host.Id })
            .HasDatabaseName("ix_discovery_run_hosts_run_id_id");
    }
}
