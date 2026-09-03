using Microsoft.EntityFrameworkCore;

using NetShield.Platform.Messaging;

namespace NetShield.Platform.Persistence;

/// <summary>
/// The platform's own tables. Today that is the transactional outbox; the append-only audit log
/// joins it in WP-0.5.
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
    }
}
