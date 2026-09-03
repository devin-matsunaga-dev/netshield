using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NetShield.Platform.Messaging;

/// <summary>
/// Maps <see cref="OutboxMessage"/> to <c>outbox_messages</c>.
/// </summary>
/// <remarks>
/// Public because a module's own <c>DbContext</c> has to map the same table to write an outbox
/// row inside its own transaction. Only <see cref="Persistence.PlatformDbContext"/> owns the
/// migration for it; a module applies this configuration and excludes the table from its own
/// migrations, so there is one table with one definition.
/// </remarks>
public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    /// <summary>The table name, so a module can exclude it from its migrations by name.</summary>
    public const string TableName = "outbox_messages";

    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName);

        builder.HasKey(message => message.Id);

        builder.Property(message => message.EventType).HasMaxLength(512).IsRequired();
        builder.Property(message => message.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(message => message.CreatedAt).IsRequired();
        builder.Property(message => message.UpdatedAt).IsRequired();
        builder.Property(message => message.Error).HasMaxLength(2000);

        // The dispatcher only ever asks one question — which rows are still pending, oldest
        // first — so the index answers exactly that and indexes nothing already delivered.
        builder.HasIndex(message => message.CreatedAt)
            .HasDatabaseName("ix_outbox_messages_pending")
            .HasFilter("processed_at IS NULL");
    }
}
