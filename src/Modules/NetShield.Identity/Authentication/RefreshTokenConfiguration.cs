using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using NetShield.Identity.Users;

namespace NetShield.Identity.Authentication;

/// <summary>Maps <see cref="RefreshToken"/> to <c>refresh_tokens</c>.</summary>
internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    internal const string TableName = "refresh_tokens";

    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName);

        builder.HasKey(token => token.Id);

        // 64 hex characters. Fixed width, so the column says what it holds.
        builder.Property(token => token.TokenHash).HasMaxLength(64).IsRequired();
        builder.Property(token => token.SessionId).IsRequired();
        builder.Property(token => token.CreatedAt).IsRequired();
        builder.Property(token => token.ExpiresAt).IsRequired();

        builder.HasOne(token => token.User)
            .WithMany()
            .HasForeignKey(token => token.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // A presented token is looked up by its digest alone, and the digest is unique because a
        // second row with the same one could only be a generator that had stopped being random.
        builder.HasIndex(token => token.TokenHash)
            .HasDatabaseName("ix_refresh_tokens_token_hash")
            .IsUnique();

        // CONVENTIONS.md §3: every foreign key has an index. This one also answers "revoke every
        // token this user holds", which a password change does.
        builder.HasIndex(token => token.UserId)
            .HasDatabaseName("ix_refresh_tokens_user_id");

        // Reuse detection revokes a whole chain at once, and that is the only query it makes.
        builder.HasIndex(token => token.SessionId)
            .HasDatabaseName("ix_refresh_tokens_session_id");
    }
}
