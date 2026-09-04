using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NetShield.Inventory.Discovery;

/// <summary>Maps <see cref="DiscoveryIgnore"/> to <c>discovery_ignores</c>.</summary>
internal sealed class DiscoveryIgnoreConfiguration : IEntityTypeConfiguration<DiscoveryIgnore>
{
    internal const string TableName = "discovery_ignores";

    /// <summary>The index carrying the one-entry-per-block guarantee.</summary>
    internal const string CidrIndexName = "ix_discovery_ignores_cidr";

    public void Configure(EntityTypeBuilder<DiscoveryIgnore> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName);

        builder.HasKey(ignore => ignore.Id);

        builder.Property(ignore => ignore.Cidr)
            .HasMaxLength(DiscoveryLimits.CidrLength).IsRequired();
        builder.Property(ignore => ignore.Reason)
            .HasMaxLength(DiscoveryLimits.ReasonLength);
        builder.Property(ignore => ignore.CreatedAt).IsRequired();
        builder.Property(ignore => ignore.UpdatedAt).IsRequired();

        // Unique on the normalised text, which is what makes 10.0.0.5 and 10.0.0.5/32 one entry
        // rather than two: both are parsed to the same block before they are stored.
        builder.HasIndex(ignore => ignore.Cidr)
            .IsUnique()
            .HasDatabaseName(CidrIndexName);
    }
}
