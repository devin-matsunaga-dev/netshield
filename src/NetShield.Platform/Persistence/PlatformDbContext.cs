using Microsoft.EntityFrameworkCore;

using NetShield.Platform.Auditing;
using NetShield.Platform.Messaging;

namespace NetShield.Platform.Persistence;

/// <summary>
/// The platform's own tables: the transactional outbox and the append-only audit log.
/// </summary>
/// <remarks>
/// A module keeps its own <c>DbContext</c> over its own tables. They share one database and one
/// connection pool (ARCHITECTURE.md §3), which is what lets a module write its domain change and
/// an outbox row in a single transaction.
/// </remarks>
public sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options) : DbContext(options)
{
    /// <summary>Events written by a domain transaction and not yet delivered.</summary>
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    // There is deliberately no DbSet<AuditEntry>. A DbSet is a handle with Remove, RemoveRange,
    // ExecuteDelete and ExecuteUpdate hanging off it, and ARCHITECTURE.md §8 says no such path
    // may exist for audit_log. NetShield.Platform.Auditing.AuditLog reaches the table through
    // Set<AuditEntry>() and only ever adds. NetShield.ArchitectureTests fails if that changes.

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new AuditEntryConfiguration());
    }
}
