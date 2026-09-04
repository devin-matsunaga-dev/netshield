using Microsoft.EntityFrameworkCore;

using NetShield.Inventory.Collector;
using NetShield.Inventory.Credentials;
using NetShield.Inventory.Devices;

using NetShield.Platform.Messaging;

namespace NetShield.Inventory.Persistence;

/// <summary>
/// The Inventory module's tables in the one NetShield database (ARCHITECTURE.md §3).
/// </summary>
/// <remarks>
/// It keeps its own migration history table, for the reason <c>IdentityDbContext</c> keeps one:
/// a module has to be able to say what it has applied without reading rows another module's
/// migrations wrote.
///
/// The <c>Devices</c> set is internal because <see cref="Device"/> is. A public handle to an
/// entity type is the boundary leak ARCHITECTURE.md §4 forbids, and
/// <c>ModuleBoundaryTests</c> fails if one appears.
/// </remarks>
public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    /// <summary>The name this context records its applied migrations under.</summary>
    public const string MigrationsHistoryTable = "__ef_migrations_history_inventory";

    /// <summary>Monitored devices, live and soft-deleted alike.</summary>
    internal DbSet<Device> Devices => Set<Device>();

    /// <summary>Credential profiles, live and soft-deleted alike.</summary>
    internal DbSet<CredentialProfile> CredentialProfiles => Set<CredentialProfile>();

    /// <summary>Which devices may be reached with which credential profile.</summary>
    internal DbSet<DeviceCredentialProfile> DeviceCredentialProfiles => Set<DeviceCredentialProfile>();

    /// <summary>Work queued for the collector fleet, pending and finished alike.</summary>
    internal DbSet<CollectorJob> CollectorJobs => Set<CollectorJob>();

    /// <summary>The collectors that have reported in, one row each.</summary>
    internal DbSet<CollectorNode> CollectorNodes => Set<CollectorNode>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfiguration(new DeviceConfiguration());
        modelBuilder.ApplyConfiguration(new CredentialProfileConfiguration());
        modelBuilder.ApplyConfiguration(new DeviceCredentialProfileConfiguration());
        modelBuilder.ApplyConfiguration(new CollectorJobConfiguration());
        modelBuilder.ApplyConfiguration(new CollectorNodeConfiguration());

        // outbox_messages, mapped here so a device write and the event describing it are one
        // transaction on one connection. NetShield.Platform owns the table and the migration
        // that creates it, so this context is told not to try (ARCHITECTURE.md §5).
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.Entity<OutboxMessage>()
            .ToTable(OutboxMessageConfiguration.TableName, table => table.ExcludeFromMigrations());
    }
}
