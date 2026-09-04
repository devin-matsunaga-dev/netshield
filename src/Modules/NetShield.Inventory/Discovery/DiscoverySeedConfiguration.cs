using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NetShield.Inventory.Discovery;

/// <summary>Maps <see cref="DiscoverySeed"/> to <c>discovery_seeds</c>.</summary>
internal sealed class DiscoverySeedConfiguration : IEntityTypeConfiguration<DiscoverySeed>
{
    internal const string TableName = "discovery_seeds";

    /// <summary>The index carrying the one uniqueness guarantee this table makes.</summary>
    internal const string NameIndexName = "ix_discovery_seeds_name_live";

    public void Configure(EntityTypeBuilder<DiscoverySeed> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName);

        builder.HasKey(seed => seed.Id);

        builder.Property(seed => seed.Name)
            .HasMaxLength(DiscoveryLimits.SeedNameLength).IsRequired();
        builder.Property(seed => seed.Description)
            .HasMaxLength(DiscoveryLimits.ReasonLength);
        builder.Property(seed => seed.Enabled).IsRequired();
        builder.Property(seed => seed.IntervalMinutes).IsRequired();
        builder.Property(seed => seed.CreatedAt).IsRequired();
        builder.Property(seed => seed.UpdatedAt).IsRequired();

        // text[], the shape devices.tags and device_fingerprints.overridden_fields already use.
        // Nothing joins on a range and nothing queries by one — the containment tests all happen
        // in memory, over a list bounded by DiscoveryLimits.MaxRangesPerSeed.
        builder.Property(seed => seed.Ranges).HasColumnType("text[]").IsRequired();
        builder.Property(seed => seed.Exclusions).HasColumnType("text[]").IsRequired();

        // Unique among live seeds only, the same shape devices.primary_ip_address uses: a
        // removed seed must release its name for the one replacing it, while the row itself has
        // to stay so that the runs naming it still resolve. Declared in raw SQL in the migration
        // for the same reason that one is — a filtered index is not expressible here.

        // What the schedule scans: the enabled seeds whose next run has fallen due.
        builder.HasIndex(seed => new { seed.Enabled, seed.NextRunAt })
            .HasDatabaseName("ix_discovery_seeds_enabled_next_run_at");

        builder.HasIndex(seed => seed.DeletedAt).HasDatabaseName("ix_discovery_seeds_deleted_at");
    }
}
