using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NetShield.Inventory.Discovery;

/// <summary>Maps <see cref="DiscoveryCandidate"/> to <c>discovery_candidates</c>.</summary>
internal sealed class DiscoveryCandidateConfiguration : IEntityTypeConfiguration<DiscoveryCandidate>
{
    internal const string TableName = "discovery_candidates";

    /// <summary>The index carrying the one-candidate-per-address guarantee.</summary>
    internal const string AddressIndexName = "ix_discovery_candidates_address";

    public void Configure(EntityTypeBuilder<DiscoveryCandidate> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName);

        builder.HasKey(candidate => candidate.Id);

        builder.Property(candidate => candidate.Address).HasColumnType("inet").IsRequired();
        builder.Property(candidate => candidate.Status)
            .HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(candidate => candidate.TimesSeen).IsRequired();
        builder.Property(candidate => candidate.FirstSeenAt).IsRequired();
        builder.Property(candidate => candidate.LastSeenAt).IsRequired();
        builder.Property(candidate => candidate.FirstSeenRunId).IsRequired();
        builder.Property(candidate => candidate.LastSeenRunId).IsRequired();
        builder.Property(candidate => candidate.CreatedAt).IsRequired();
        builder.Property(candidate => candidate.UpdatedAt).IsRequired();

        // Unique across the whole table, not only among the undecided ones. That is what makes a
        // re-run update rather than duplicate, and it is also what stops an ignored address
        // coming back as a second row for the same host. Declared in raw SQL in the migration,
        // because EF will not index a property whose CLR type is not comparable and IPAddress is
        // not — the same trade devices.primary_ip_address makes for the same reason.

        // The review list's default order, and the one every keyset page walks.
        builder.HasIndex(candidate => new { candidate.Status, candidate.LastSeenAt, candidate.Id })
            .HasDatabaseName("ix_discovery_candidates_status_last_seen_at_id");
    }
}
