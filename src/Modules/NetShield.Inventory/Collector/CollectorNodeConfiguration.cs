using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NetShield.Inventory.Collector;

/// <summary>Maps <see cref="CollectorNode"/> to <c>collectors</c>.</summary>
internal sealed class CollectorNodeConfiguration : IEntityTypeConfiguration<CollectorNode>
{
    internal const string TableName = "collectors";

    /// <summary>The index that makes one collector one row across its restarts.</summary>
    internal const string NameIndexName = "ix_collectors_normalized_name";

    public void Configure(EntityTypeBuilder<CollectorNode> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName);

        builder.HasKey(node => node.Id);

        builder.Property(node => node.Name).HasMaxLength(CollectorLimits.NameLength).IsRequired();
        builder.Property(node => node.NormalizedName).HasMaxLength(CollectorLimits.NameLength).IsRequired();
        builder.Property(node => node.Version).HasMaxLength(CollectorLimits.VersionLength);

        builder.Property(node => node.LastSeenAt).IsRequired();
        builder.Property(node => node.CreatedAt).IsRequired();
        builder.Property(node => node.UpdatedAt).IsRequired();

        // Unique with no live filter, unlike the inventory tables: a collector is not soft-deleted
        // and a name is not released, because the row is the history of that collector's
        // liveness rather than a thing an operator created.
        builder.HasIndex(node => node.NormalizedName)
            .IsUnique()
            .HasDatabaseName(NameIndexName);

        // The system-health question: which collectors have gone quiet.
        builder.HasIndex(node => node.LastSeenAt)
            .HasDatabaseName("ix_collectors_last_seen_at");
    }
}
