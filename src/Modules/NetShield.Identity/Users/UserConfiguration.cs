using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NetShield.Identity.Users;

/// <summary>Maps <see cref="User"/> to <c>users</c>.</summary>
internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    internal const string TableName = "users";

    public void Configure(EntityTypeBuilder<User> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName);

        builder.HasKey(user => user.Id);

        builder.Property(user => user.Username).HasMaxLength(64).IsRequired();
        builder.Property(user => user.NormalizedUsername).HasMaxLength(64).IsRequired();
        builder.Property(user => user.DisplayName).HasMaxLength(128).IsRequired();
        builder.Property(user => user.Email).HasMaxLength(320);

        // The PHC string for the configured costs is well under 256; the column is sized for a
        // future algorithm change rather than for today's format.
        builder.Property(user => user.PasswordHash).HasMaxLength(512).IsRequired();

        // Stored as the role's name rather than its ordinal, so that adding a role in WP-0.5
        // cannot silently renumber the rows already written.
        builder.Property(user => user.Role).HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.Property(user => user.CreatedAt).IsRequired();
        builder.Property(user => user.UpdatedAt).IsRequired();
        builder.Property(user => user.PasswordChangedAt).IsRequired();

        // Sign-in looks up by this and nothing else, and the index is what makes two accounts
        // differing only in case impossible rather than merely discouraged.
        builder.HasIndex(user => user.NormalizedUsername)
            .HasDatabaseName("ix_users_normalized_username")
            .IsUnique();
    }
}
