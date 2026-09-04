using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NetShield.Inventory.Discovery;

/// <summary>Maps <see cref="DiscoveryRun"/> to <c>discovery_runs</c>.</summary>
internal sealed class DiscoveryRunConfiguration : IEntityTypeConfiguration<DiscoveryRun>
{
    internal const string TableName = "discovery_runs";

    public void Configure(EntityTypeBuilder<DiscoveryRun> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName);

        builder.HasKey(run => run.Id);

        builder.Property(run => run.SeedId).IsRequired();
        builder.Property(run => run.SeedName)
            .HasMaxLength(DiscoveryLimits.SeedNameLength).IsRequired();

        // Stored as the member's name rather than its ordinal, so that adding a trigger or a
        // status in a later package cannot silently renumber the rows already written.
        builder.Property(run => run.Trigger).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(run => run.Status).HasConversion<string>().HasMaxLength(16).IsRequired();

        builder.Property(run => run.Ranges).HasColumnType("text[]").IsRequired();
        builder.Property(run => run.Exclusions).HasColumnType("text[]").IsRequired();

        builder.Property(run => run.AddressCount).IsRequired();
        builder.Property(run => run.JobCount).IsRequired();
        builder.Property(run => run.JobsCompleted).IsRequired();
        builder.Property(run => run.JobsFailed).IsRequired();
        builder.Property(run => run.RespondedCount).IsRequired();
        builder.Property(run => run.NewCandidateCount).IsRequired();
        builder.Property(run => run.KnownCandidateCount).IsRequired();
        builder.Property(run => run.ExistingDeviceCount).IsRequired();
        builder.Property(run => run.IgnoredCount).IsRequired();
        builder.Property(run => run.StartedAt).IsRequired();
        builder.Property(run => run.CreatedAt).IsRequired();
        builder.Property(run => run.UpdatedAt).IsRequired();

        // No foreign key to discovery_seeds, for the reason collector_jobs declares none to
        // devices: the seed soft-deletes, so a cascade would erase the history of what was swept
        // and a restrict would block the removal. The run keeps the seed's name for that reason.

        // The history list's default sort, and the one every keyset page walks.
        builder.HasIndex(run => new { run.StartedAt, run.Id })
            .HasDatabaseName("ix_discovery_runs_started_at_id");

        // What "does this seed already have a run in flight" reads.
        builder.HasIndex(run => new { run.SeedId, run.Status })
            .HasDatabaseName("ix_discovery_runs_seed_id_status");
    }
}
