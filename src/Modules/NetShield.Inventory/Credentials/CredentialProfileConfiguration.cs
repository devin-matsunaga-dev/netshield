using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NetShield.Inventory.Credentials;

/// <summary>Maps <see cref="CredentialProfile"/> to <c>credential_profiles</c>.</summary>
internal sealed class CredentialProfileConfiguration : IEntityTypeConfiguration<CredentialProfile>
{
    internal const string TableName = "credential_profiles";

    /// <summary>The index carrying the one uniqueness guarantee this table makes.</summary>
    internal const string NameIndexName = "ix_credential_profiles_normalized_name_live";

    public void Configure(EntityTypeBuilder<CredentialProfile> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName);

        builder.HasKey(profile => profile.Id);

        builder.Property(profile => profile.Name)
            .HasMaxLength(CredentialLimits.NameLength)
            .IsRequired();

        builder.Property(profile => profile.NormalizedName)
            .HasMaxLength(CredentialLimits.NameLength)
            .IsRequired();

        builder.Property(profile => profile.Description).HasMaxLength(CredentialLimits.DescriptionLength);
        builder.Property(profile => profile.Username).HasMaxLength(CredentialLimits.UsernameLength);

        // Stored as the member's name rather than its ordinal, so adding an algorithm in a later
        // package cannot silently renumber the rows already written.
        builder.Property(profile => profile.Kind).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(profile => profile.AuthAlgorithm).HasConversion<string>().HasMaxLength(16);
        builder.Property(profile => profile.PrivacyAlgorithm).HasConversion<string>().HasMaxLength(16);

        builder.Property(profile => profile.KeyId).HasMaxLength(CredentialLimits.KeyIdLength).IsRequired();

        // bytea. The sealed material and the sealed data key, and nothing a query can read
        // anything out of without the key-encryption key the database does not hold.
        builder.Property(profile => profile.WrappedDataKey).HasColumnType("bytea").IsRequired();
        builder.Property(profile => profile.MaterialCiphertext).HasColumnType("bytea").IsRequired();

        // Composed from the three columns above; there is nothing of its own to store.
        builder.Ignore(profile => profile.Ciphertext);

        builder.Property(profile => profile.MaterialUpdatedAt).IsRequired();
        builder.Property(profile => profile.CreatedAt).IsRequired();
        builder.Property(profile => profile.UpdatedAt).IsRequired();

        // Unique among live profiles only, for the reason a device's address is: a removed
        // profile must release its name for the replacement, while its row stays so the audit
        // rows naming it still resolve. Declarable here, unlike the device index, because a
        // string is comparable and EF will index it.
        builder.HasIndex(profile => profile.NormalizedName)
            .IsUnique()
            .HasFilter("deleted_at IS NULL")
            .HasDatabaseName(NameIndexName);

        builder.HasIndex(profile => new { profile.CreatedAt, profile.Id })
            .HasDatabaseName("ix_credential_profiles_created_at_id");

        builder.HasIndex(profile => profile.DeletedAt)
            .HasDatabaseName("ix_credential_profiles_deleted_at");

        // The rows a rotation has left to do. A partial index would be narrower but would have to
        // name the active key, which is configuration and changes.
        builder.HasIndex(profile => profile.KeyId)
            .HasDatabaseName("ix_credential_profiles_key_id");
    }
}
