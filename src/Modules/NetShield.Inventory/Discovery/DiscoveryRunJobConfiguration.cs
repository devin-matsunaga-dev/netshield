using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NetShield.Inventory.Discovery;

/// <summary>Maps <see cref="DiscoveryRunJob"/> to <c>discovery_run_jobs</c>.</summary>
internal sealed class DiscoveryRunJobConfiguration : IEntityTypeConfiguration<DiscoveryRunJob>
{
    internal const string TableName = "discovery_run_jobs";

    /// <summary>The index carrying the one-run-per-job guarantee.</summary>
    internal const string CollectorJobIndexName = "ix_discovery_run_jobs_collector_job_id";

    public void Configure(EntityTypeBuilder<DiscoveryRunJob> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName);

        builder.HasKey(job => job.Id);

        builder.Property(job => job.RunId).IsRequired();
        builder.Property(job => job.CollectorJobId).IsRequired();
        builder.Property(job => job.Sequence).IsRequired();

        // Text rather than inet. Both are addresses, but they are the ends of a span rather than
        // things anything queries by, and keeping them as the collector was told them is what
        // makes the row readable beside the job's own parameters.
        builder.Property(job => job.FirstAddress)
            .HasMaxLength(DiscoveryLimits.CidrLength).IsRequired();
        builder.Property(job => job.LastAddress)
            .HasMaxLength(DiscoveryLimits.CidrLength).IsRequired();

        builder.Property(job => job.AddressCount).IsRequired();
        builder.Property(job => job.CreatedAt).IsRequired();
        builder.Property(job => job.UpdatedAt).IsRequired();

        // Unique: a queued job belongs to one run, and this is what the result handler looks a
        // job up by to find out whether it is one of ours at all.
        builder.HasIndex(job => job.CollectorJobId)
            .IsUnique()
            .HasDatabaseName(CollectorJobIndexName);

        // What "is this run finished" reads: the rows of one run that have not been applied.
        builder.HasIndex(job => new { job.RunId, job.AppliedAt })
            .HasDatabaseName("ix_discovery_run_jobs_run_id_applied_at");
    }
}
