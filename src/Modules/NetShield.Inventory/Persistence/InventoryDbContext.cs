using Microsoft.EntityFrameworkCore;

using NetShield.Inventory.Clients;
using NetShield.Inventory.Collector;
using NetShield.Inventory.Credentials;
using NetShield.Inventory.Devices;
using NetShield.Inventory.Discovery;
using NetShield.Inventory.Reachability;
using NetShield.Inventory.Topology;

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

    /// <summary>What is known about each device's reachability, one row per device.</summary>
    internal DbSet<DeviceReachability> DeviceReachabilities => Set<DeviceReachability>();

    /// <summary>What the last SNMP walk established about each device, one row per device.</summary>
    internal DbSet<DeviceFingerprint> DeviceFingerprints => Set<DeviceFingerprint>();

    /// <summary>The interfaces the last walk found, one row per interface per device.</summary>
    internal DbSet<DeviceInterface> DeviceInterfaces => Set<DeviceInterface>();

    /// <summary>What the discovery schedule sweeps, live and soft-deleted alike.</summary>
    internal DbSet<DiscoverySeed> DiscoverySeeds => Set<DiscoverySeed>();

    /// <summary>Every discovery run, in flight and finished alike.</summary>
    internal DbSet<DiscoveryRun> DiscoveryRuns => Set<DiscoveryRun>();

    /// <summary>The sweep jobs each run fanned out into.</summary>
    internal DbSet<DiscoveryRunJob> DiscoveryRunJobs => Set<DiscoveryRunJob>();

    /// <summary>The addresses that answered each run, one row per responder.</summary>
    internal DbSet<DiscoveryRunHost> DiscoveryRunHosts => Set<DiscoveryRunHost>();

    /// <summary>Addresses a sweep found that are not devices, one row per address.</summary>
    internal DbSet<DiscoveryCandidate> DiscoveryCandidates => Set<DiscoveryCandidate>();

    /// <summary>Blocks discovery will never offer as candidates.</summary>
    internal DbSet<DiscoveryIgnore> DiscoveryIgnores => Set<DiscoveryIgnore>();

    /// <summary>Endpoints observed on the network, one row per hardware address.</summary>
    internal DbSet<Client> Clients => Set<Client>();

    /// <summary>
    /// Which client held which address, and when. The table every time-accurate resolution
    /// reads, and the reason its invariants are indexes rather than conventions.
    /// </summary>
    internal DbSet<ClientIpBinding> ClientIpBindings => Set<ClientIpBinding>();

    /// <summary>Which device's port reported which client, and when.</summary>
    internal DbSet<ClientPortBinding> ClientPortBindings => Set<ClientPortBinding>();

    /// <summary>What reading each device's client tables established, one row per device.</summary>
    internal DbSet<DeviceClientScan> DeviceClientScans => Set<DeviceClientScan>();

    /// <summary>
    /// What each protocol said is on the other end of each interface — the raw evidence an
    /// adjacency is reconciled from.
    /// </summary>
    internal DbSet<DeviceNeighbor> DeviceNeighbors => Set<DeviceNeighbor>();

    /// <summary>
    /// The topology graph's edges, one row per link. ARCHITECTURE.md §1: relationships are
    /// adjacency tables in PostgreSQL, and one hop is all V1 builds.
    /// </summary>
    internal DbSet<DeviceAdjacency> DeviceAdjacencies => Set<DeviceAdjacency>();

    /// <summary>What reading each device's topology established, one row per device.</summary>
    internal DbSet<DeviceTopologyScan> DeviceTopologyScans => Set<DeviceTopologyScan>();

    /// <summary>
    /// The VLANs each device is configured with — the observation WP-2.2 records, and the only
    /// table behind the estate-wide VLAN inventory, which is a grouping of these rows rather than
    /// a table of its own.
    /// </summary>
    internal DbSet<DeviceVlan> DeviceVlans => Set<DeviceVlan>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfiguration(new DeviceConfiguration());
        modelBuilder.ApplyConfiguration(new CredentialProfileConfiguration());
        modelBuilder.ApplyConfiguration(new DeviceCredentialProfileConfiguration());
        modelBuilder.ApplyConfiguration(new CollectorJobConfiguration());
        modelBuilder.ApplyConfiguration(new CollectorNodeConfiguration());
        modelBuilder.ApplyConfiguration(new DeviceReachabilityConfiguration());
        modelBuilder.ApplyConfiguration(new DeviceFingerprintConfiguration());
        modelBuilder.ApplyConfiguration(new DeviceInterfaceConfiguration());
        modelBuilder.ApplyConfiguration(new DiscoverySeedConfiguration());
        modelBuilder.ApplyConfiguration(new DiscoveryRunConfiguration());
        modelBuilder.ApplyConfiguration(new DiscoveryRunJobConfiguration());
        modelBuilder.ApplyConfiguration(new DiscoveryRunHostConfiguration());
        modelBuilder.ApplyConfiguration(new DiscoveryCandidateConfiguration());
        modelBuilder.ApplyConfiguration(new DiscoveryIgnoreConfiguration());
        modelBuilder.ApplyConfiguration(new ClientConfiguration());
        modelBuilder.ApplyConfiguration(new ClientIpBindingConfiguration());
        modelBuilder.ApplyConfiguration(new ClientPortBindingConfiguration());
        modelBuilder.ApplyConfiguration(new DeviceClientScanConfiguration());
        modelBuilder.ApplyConfiguration(new DeviceNeighborConfiguration());
        modelBuilder.ApplyConfiguration(new DeviceAdjacencyConfiguration());
        modelBuilder.ApplyConfiguration(new DeviceTopologyScanConfiguration());
        modelBuilder.ApplyConfiguration(new DeviceVlanConfiguration());

        // outbox_messages, mapped here so a device write and the event describing it are one
        // transaction on one connection. NetShield.Platform owns the table and the migration
        // that creates it, so this context is told not to try (ARCHITECTURE.md §5).
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.Entity<OutboxMessage>()
            .ToTable(OutboxMessageConfiguration.TableName, table => table.ExcludeFromMigrations());
    }
}
