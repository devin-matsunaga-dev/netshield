using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NetShield.Platform.Auditing;

/// <summary>
/// Maps <see cref="AuditEntry"/> to <c>audit_log</c>.
/// </summary>
/// <remarks>
/// The table is a plain relational table, not a hypertable. It is written once per state-changing
/// call rather than thousands of times a second, it is read by identity and by target far more
/// often than by time window, and ARCHITECTURE.md §3 reserves hypertables for metrics, flows and
/// log events.
/// </remarks>
public sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    /// <summary>The table name, as ARCHITECTURE.md §8 and the WP-0.5 entry spell it.</summary>
    public const string TableName = "audit_log";

    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName);

        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.CreatedAt).IsRequired();
        builder.Property(entry => entry.ActorUsername).HasMaxLength(256);
        builder.Property(entry => entry.ActorRole).HasConversion<string>().HasMaxLength(32);
        builder.Property(entry => entry.SourceIp).HasMaxLength(64);
        builder.Property(entry => entry.Action).HasMaxLength(128).IsRequired();
        builder.Property(entry => entry.TargetType).HasMaxLength(64);
        builder.Property(entry => entry.TargetId).HasMaxLength(256);
        builder.Property(entry => entry.Outcome).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(entry => entry.Before).HasColumnType("jsonb");
        builder.Property(entry => entry.After).HasColumnType("jsonb");
        builder.Property(entry => entry.HttpMethod).HasMaxLength(16).IsRequired();
        builder.Property(entry => entry.Path).HasMaxLength(2048).IsRequired();
        builder.Property(entry => entry.StatusCode).IsRequired();
        builder.Property(entry => entry.TraceId).HasMaxLength(64);

        // The three questions an audit log is asked: what happened lately, what did this account
        // do, and what has been done to this thing.
        builder.HasIndex(entry => entry.CreatedAt)
            .HasDatabaseName("ix_audit_log_created_at")
            .IsDescending();

        builder.HasIndex(entry => entry.ActorUserId)
            .HasDatabaseName("ix_audit_log_actor_user_id");

        builder.HasIndex(entry => new { entry.TargetType, entry.TargetId })
            .HasDatabaseName("ix_audit_log_target");
    }
}
