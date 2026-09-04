using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NetShield.Inventory.Collector;

/// <summary>Maps <see cref="CollectorJob"/> to <c>collector_jobs</c>.</summary>
internal sealed class CollectorJobConfiguration : IEntityTypeConfiguration<CollectorJob>
{
    internal const string TableName = "collector_jobs";

    /// <summary>The index the lease query claims through.</summary>
    internal const string ClaimableIndexName = "ix_collector_jobs_claimable";

    public void Configure(EntityTypeBuilder<CollectorJob> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName);

        builder.HasKey(job => job.Id);

        // Stored as the member's name rather than its ordinal, so that adding a kind or a status
        // in a later package cannot renumber a row already queued.
        builder.Property(job => job.Kind).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(job => job.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(job => job.Outcome).HasConversion<string>().HasMaxLength(16);

        builder.Property(job => job.Parameters).HasColumnType("jsonb");
        builder.Property(job => job.Result).HasColumnType("jsonb");

        builder.Property(job => job.LeaseToken).HasMaxLength(CollectorLimits.LeaseTokenLength);
        builder.Property(job => job.LeasedBy).HasMaxLength(CollectorLimits.NameLength);
        builder.Property(job => job.Detail).HasMaxLength(CollectorLimits.DetailLength);

        builder.Property(job => job.DueAt).IsRequired();
        builder.Property(job => job.CreatedAt).IsRequired();
        builder.Property(job => job.UpdatedAt).IsRequired();

        // The one query in the hot path: the due, unfinished work in the order it fell due. The
        // status column leads because the completed rows are the ones that accumulate, and the
        // index should stop describing them as soon as they do.
        builder.HasIndex(job => new { job.Status, job.DueAt })
            .HasDatabaseName(ClaimableIndexName);

        // "What has this device been asked to do", which is what the device screen and every
        // later collection package reads.
        builder.HasIndex(job => new { job.DeviceId, job.CreatedAt })
            .HasDatabaseName("ix_collector_jobs_device_id_created_at");

        // Not a foreign key, on purpose. A device or a credential profile is soft-deleted while
        // its rows survive (WP-1.1, WP-1.2), and a job is a historical record of work that named
        // one — a cascade would delete the history and a restrict would block the delete.
        builder.HasIndex(job => job.CredentialProfileId)
            .HasDatabaseName("ix_collector_jobs_credential_profile_id");
    }
}
