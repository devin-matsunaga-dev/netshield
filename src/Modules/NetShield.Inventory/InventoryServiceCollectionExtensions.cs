using FluentValidation;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

using NetShield.Contracts.Collector.Events;
using NetShield.Contracts.Inventory;
using NetShield.Contracts.Inventory.Events;

using NetShield.Inventory.Clients;
using NetShield.Inventory.Clients.Handlers;
using NetShield.Inventory.Collector;
using NetShield.Inventory.Collector.Contract;
using NetShield.Inventory.Collector.Handlers;
using NetShield.Inventory.Credentials;
using NetShield.Inventory.Credentials.Handlers;
using NetShield.Inventory.Devices.Handlers;
using NetShield.Inventory.Discovery;
using NetShield.Inventory.Discovery.Handlers;
using NetShield.Inventory.Endpoints;
using NetShield.Inventory.Reachability;
using NetShield.Inventory.Reachability.Handlers;
using NetShield.Inventory.Resolution;
using NetShield.Inventory.Topology;
using NetShield.Inventory.Topology.Handlers;

using NetShield.Platform;
using NetShield.Platform.Authentication;
using NetShield.Platform.Messaging;

namespace NetShield.Inventory;

/// <summary>
/// Registers the Inventory module: the device, credential and collector handlers, their
/// validators, the envelope encryption a credential is sealed with, the shared secret the
/// internal collector contract authenticates by, and the integration events this module can
/// publish.
/// </summary>
/// <remarks>
/// The <c>InventoryDbContext</c> is registered by the composition root, not here, because only
/// the composition root knows where the database is (SPEC.md §5).
/// </remarks>
public static class InventoryServiceCollectionExtensions
{
    /// <summary>
    /// Adds everything the Inventory module needs, and maps nothing. The host must also call
    /// <c>AddNetShieldAuthorization()</c> and <c>AddNetShieldAudit()</c>; the handlers here rely
    /// on the resource guard and the audit context those register.
    /// </summary>
    public static TBuilder AddNetShieldInventory<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Registering the module and being able to seal what it stores are one act. A host that
        // added the handlers without the key ring would start, pass its health checks, and fail
        // the first request that touched a credential (ARCHITECTURE.md §8).
        builder.AddNetShieldEnvelopeEncryption();

        // And the same act for the internal contract: this module serves /internal/collector, so
        // registering the module is what makes the shared secret required and the routes
        // authenticable. It keeps the secret out of the schema step, which serves nothing.
        builder.AddNetShieldCollectorAuthentication();

        builder.Services.AddOptions<CollectorJobOptions>()
            .Bind(builder.Configuration.GetSection(CollectorJobOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services.AddOptions<ReachabilityOptions>()
            .Bind(builder.Configuration.GetSection(ReachabilityOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services.AddOptions<DiscoveryOptions>()
            .Bind(builder.Configuration.GetSection(DiscoveryOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services.AddOptions<ClientOptions>()
            .Bind(builder.Configuration.GetSection(ClientOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services.AddOptions<TopologyOptions>()
            .Bind(builder.Configuration.GetSection(TopologyOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services.TryAddScoped<GetDeviceListHandler>();
        builder.Services.TryAddScoped<GetDeviceHandler>();
        builder.Services.TryAddScoped<CreateDeviceHandler>();
        builder.Services.TryAddScoped<UpdateDeviceHandler>();
        builder.Services.TryAddScoped<DeleteDeviceHandler>();
        builder.Services.TryAddScoped<GetDeviceFingerprintHandler>();
        builder.Services.TryAddScoped<GetDeviceInterfaceListHandler>();
        builder.Services.TryAddScoped<GetDeviceReachabilityHandler>();

        builder.Services.TryAddScoped<CredentialMaterialProtector>();
        builder.Services.TryAddScoped<GetCredentialProfileListHandler>();
        builder.Services.TryAddScoped<GetCredentialProfileHandler>();
        builder.Services.TryAddScoped<CreateCredentialProfileHandler>();
        builder.Services.TryAddScoped<UpdateCredentialProfileHandler>();
        builder.Services.TryAddScoped<ReplaceCredentialMaterialHandler>();
        builder.Services.TryAddScoped<DeleteCredentialProfileHandler>();
        builder.Services.TryAddScoped<GetDeviceCredentialProfilesHandler>();
        builder.Services.TryAddScoped<SetDeviceCredentialProfilesHandler>();

        // The decrypt path. Its one production caller is LeaseCollectorJobsHandler below; the
        // interface is still internal to this module, so nothing outside it can name the type to
        // ask for one (WP-1.2 left this decision to WP-1.3, and the answer was not to widen it).
        builder.Services.TryAddScoped<ICredentialResolver, CredentialResolver>();

        builder.Services.TryAddScoped<ICollectorJobQueue, CollectorJobQueue>();
        builder.Services.TryAddScoped<LeaseCollectorJobsHandler>();
        builder.Services.TryAddScoped<SubmitCollectorResultsHandler>();
        builder.Services.TryAddScoped<RecordHeartbeatHandler>();

        // The reachability schedule and the subscriber that reads what it produced. The pass is
        // registered here; the loop that drives it is the separate opt-in below, because exactly
        // one process may schedule work for the estate.
        builder.Services.TryAddScoped<ReachabilitySchedulePass>();
        builder.Services.AddScoped<IIntegrationEventHandler<CollectorJobCompleted>,
            RecordReachabilityResultHandler>();

        // The on-demand fingerprint walk, and the second subscriber to CollectorJobCompleted.
        // The three subscribers each read only the jobs their own package queued: one filters on
        // a Poll naming the ICMP probe, one on a Discover naming the SNMP walk, and one on a
        // Discover that discovery_run_jobs says belongs to a run.
        builder.Services.TryAddScoped<SnmpCredentialSelector>();
        builder.Services.TryAddScoped<QueueDeviceWalkHandler>();
        builder.Services.AddScoped<IIntegrationEventHandler<CollectorJobCompleted>,
            RecordSnmpWalkResultHandler>();

        // Discovery: the seeds an operator maintains, the runs they produce, the candidates a run
        // leaves for review, the permanent ignore list, and the third subscriber. The loop that
        // drives the schedule is the separate opt-in below, for the reason the reachability one
        // is separate.
        builder.Services.TryAddScoped<DiscoveryRunLauncher>();
        builder.Services.TryAddScoped<DiscoverySchedulePass>();
        builder.Services.TryAddScoped<GetDiscoverySeedListHandler>();
        builder.Services.TryAddScoped<GetDiscoverySeedHandler>();
        builder.Services.TryAddScoped<CreateDiscoverySeedHandler>();
        builder.Services.TryAddScoped<UpdateDiscoverySeedHandler>();
        builder.Services.TryAddScoped<DeleteDiscoverySeedHandler>();
        builder.Services.TryAddScoped<StartDiscoveryRunHandler>();
        builder.Services.TryAddScoped<GetDiscoveryRunListHandler>();
        builder.Services.TryAddScoped<GetDiscoveryRunHandler>();
        builder.Services.TryAddScoped<GetDiscoveryRunHostListHandler>();
        builder.Services.TryAddScoped<GetDiscoveryCandidateListHandler>();
        builder.Services.TryAddScoped<PromoteDiscoveryCandidateHandler>();
        builder.Services.TryAddScoped<IgnoreDiscoveryCandidateHandler>();
        builder.Services.TryAddScoped<GetDiscoveryIgnoreListHandler>();
        builder.Services.TryAddScoped<CreateDiscoveryIgnoreHandler>();
        builder.Services.TryAddScoped<DeleteDiscoveryIgnoreHandler>();
        builder.Services.AddScoped<IIntegrationEventHandler<CollectorJobCompleted>,
            RecordRangeSweepResultHandler>();

        // Client tracking: the schedule that reads the estate's ARP and forwarding tables, the
        // fourth CollectorJobCompleted subscriber that folds what they said into closed
        // intervals, and the resolution every downstream enrichment leans on. The loop that
        // drives the schedule is the separate opt-in below, for the reason the other two are.
        builder.Services.TryAddScoped<ClientSchedulePass>();
        builder.Services.TryAddScoped<ClientObservationApplier>();
        builder.Services.TryAddScoped<QueueClientWalkHandler>();
        builder.Services.TryAddScoped<GetClientListHandler>();
        builder.Services.TryAddScoped<GetClientHandler>();
        builder.Services.TryAddScoped<GetClientIpHistoryHandler>();
        builder.Services.TryAddScoped<GetClientPortHistoryHandler>();
        builder.Services.TryAddScoped<ResolveAssetHandler>();
        builder.Services.AddScoped<IIntegrationEventHandler<CollectorJobCompleted>,
            RecordClientWalkResultHandler>();

        // Topology: the schedule that reads the estate's neighbour protocols, routing tables and
        // VLAN tables, the three CollectorJobCompleted subscribers that fold what they said into
        // edges and VLAN rows, and the resolver that turns "something called core-sw-1" into a
        // device NetShield already has. Three subscribers rather than one because they are three
        // walks — a routing table or a Q-BRIDGE table that times out must not discard the edges
        // the neighbour walk established. The loop that drives the schedule is the separate
        // opt-in below, for the reason the other three are.
        builder.Services.TryAddScoped<TopologySchedulePass>();
        builder.Services.TryAddScoped<TopologyResolver>();
        builder.Services.TryAddScoped<NeighborObservationApplier>();
        builder.Services.TryAddScoped<TopologyWalkResultReader>();
        builder.Services.TryAddScoped<QueueTopologyWalkHandler>();
        builder.Services.TryAddScoped<GetDeviceAdjacencyListHandler>();
        builder.Services.TryAddScoped<GetDeviceTopologyScanHandler>();
        builder.Services.AddScoped<IIntegrationEventHandler<CollectorJobCompleted>,
            RecordNeighborWalkResultHandler>();
        builder.Services.AddScoped<IIntegrationEventHandler<CollectorJobCompleted>,
            RecordRouteWalkResultHandler>();

        // The VLAN inventory. One table and no conclusion table beside it: the estate-wide view is
        // a grouping of the device rows plus two joins, all of it derived on read (WP-2.2).
        builder.Services.TryAddScoped<VlanObservationApplier>();
        builder.Services.TryAddScoped<VlanReadJoins>();
        builder.Services.TryAddScoped<GetVlanListHandler>();
        builder.Services.TryAddScoped<GetVlanHandler>();
        builder.Services.TryAddScoped<GetDeviceVlanListHandler>();
        builder.Services.AddScoped<IIntegrationEventHandler<CollectorJobCompleted>,
            RecordVlanWalkResultHandler>();

        // ResolveAssetAt. Internal to the module, the way the credential resolver is: nothing
        // outside NetShield.Inventory can name the type to ask for one, and its read surface is
        // GET /api/v1/clients/resolve.
        builder.Services.TryAddScoped<IAssetResolver, AssetResolver>();

        // And the half of the cache that keeps it honest. A device arriving at an address,
        // leaving one, or moving between two makes the cached history for those addresses wrong;
        // DeviceUpdated carries the previous address so that the entry a device has just vacated
        // can be named at all (WP-1.1 wrote that member for this).
        builder.Services.AddScoped<IIntegrationEventHandler<DeviceCreated>, AssetCacheInvalidator>();
        builder.Services.AddScoped<IIntegrationEventHandler<DeviceUpdated>, AssetCacheInvalidator>();
        builder.Services.AddScoped<IIntegrationEventHandler<DeviceRemoved>, AssetCacheInvalidator>();

        builder.Services.TryAddScoped<IValidator<CreateDeviceRequest>, CreateDeviceRequestValidator>();
        builder.Services.TryAddScoped<IValidator<UpdateDeviceRequest>, UpdateDeviceRequestValidator>();

        builder.Services.TryAddScoped<IValidator<CreateCredentialProfileRequest>,
            CreateCredentialProfileRequestValidator>();
        builder.Services.TryAddScoped<IValidator<UpdateCredentialProfileRequest>,
            UpdateCredentialProfileRequestValidator>();
        builder.Services.TryAddScoped<IValidator<ReplaceCredentialMaterialRequest>,
            ReplaceCredentialMaterialRequestValidator>();
        builder.Services.TryAddScoped<IValidator<SetDeviceCredentialProfilesRequest>,
            SetDeviceCredentialProfilesRequestValidator>();

        builder.Services.TryAddScoped<IValidator<CreateDiscoverySeedRequest>,
            CreateDiscoverySeedRequestValidator>();
        builder.Services.TryAddScoped<IValidator<UpdateDiscoverySeedRequest>,
            UpdateDiscoverySeedRequestValidator>();
        builder.Services.TryAddScoped<IValidator<CreateDiscoveryIgnoreRequest>,
            CreateDiscoveryIgnoreRequestValidator>();
        builder.Services.TryAddScoped<IValidator<PromoteDiscoveryCandidateRequest>,
            PromoteDiscoveryCandidateRequestValidator>();

        builder.Services.TryAddScoped<IValidator<CollectorResultsRequest>,
            CollectorResultsRequestValidator>();
        builder.Services.TryAddScoped<IValidator<CollectorHeartbeatRequest>,
            CollectorHeartbeatRequestValidator>();

        // Declared here rather than at the composition root, so that a module and the events it
        // publishes arrive together. An event the registry does not know is refused at the write
        // rather than becoming a row nothing can read back.
        builder.Services.AddIntegrationEvent<DeviceCreated>();
        builder.Services.AddIntegrationEvent<DeviceUpdated>();
        builder.Services.AddIntegrationEvent<DeviceRemoved>();
        builder.Services.AddIntegrationEvent<CredentialProfileCreated>();
        builder.Services.AddIntegrationEvent<CredentialProfileUpdated>();
        builder.Services.AddIntegrationEvent<CredentialProfileRemoved>();
        builder.Services.AddIntegrationEvent<DeviceCredentialProfilesChanged>();
        builder.Services.AddIntegrationEvent<CollectorJobCompleted>();
        builder.Services.AddIntegrationEvent<DeviceStateChanged>();
        builder.Services.AddIntegrationEvent<DeviceFingerprinted>();
        builder.Services.AddIntegrationEvent<DeviceDiscovered>();
        builder.Services.AddIntegrationEvent<DiscoveryRunStarted>();
        builder.Services.AddIntegrationEvent<DiscoveryRunCompleted>();
        builder.Services.AddIntegrationEvent<ClientDiscovered>();
        builder.Services.AddIntegrationEvent<DeviceAdjacencyChanged>();
        builder.Services.AddIntegrationEvent<DeviceVlansChanged>();

        builder.Services.ConfigureHttpJsonOptions(json =>
        {
            json.SerializerOptions.TypeInfoResolverChain.Insert(0, InventorySerializerContext.Default);

            // The internal contract's shapes, kept in a context of their own because they include
            // an opened credential and the public context describes what the SPA is generated
            // from.
            json.SerializerOptions.TypeInfoResolverChain.Insert(1, CollectorSerializerContext.Default);
        });

        return builder;
    }

    /// <summary>
    /// Starts the reachability schedule: the loop that queues an ICMP probe for every device
    /// whose next one has fallen due.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="AddNetShieldInventory{TBuilder}"/> for the reason
    /// <c>AddOutboxDispatcher</c> is separate from <c>AddNetShieldPlatform</c>: registering a
    /// module says what it can do, and deciding that <em>this</em> process is the one that tells
    /// five hundred devices what to expect is a choice that belongs in the diff at the
    /// composition root. The schema step registers the module and must not start scheduling on
    /// its way past.
    /// </remarks>
    public static TBuilder AddNetShieldReachabilityScheduler<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddHostedService<ReachabilityScheduler>();

        return builder;
    }

    /// <summary>
    /// Starts the discovery schedule: the loop that begins a run for every seed whose next one
    /// has fallen due.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="AddNetShieldInventory{TBuilder}"/> for the reason
    /// <see cref="AddNetShieldReachabilityScheduler{TBuilder}"/> is separate: exactly one process
    /// should decide what the estate is asked to do, and sweeping the address space is the most
    /// conspicuous thing NetShield does without being asked. The schema step registers the module
    /// on its way past and must not start sweeping.
    /// </remarks>
    public static TBuilder AddNetShieldDiscoveryScheduler<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddHostedService<DiscoveryScheduler>();

        return builder;
    }

    /// <summary>
    /// Starts the client schedule: the loop that reads each device's ARP and forwarding tables
    /// so that <c>ResolveAssetAt</c> has something to resolve through.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="AddNetShieldInventory{TBuilder}"/> for the reason the other two
    /// schedulers are separate: exactly one process should decide what the estate is asked to do,
    /// and the schema step registers the module on its way past without becoming a third thing
    /// walking five hundred devices.
    /// </remarks>
    public static TBuilder AddNetShieldClientScheduler<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddHostedService<ClientScheduler>();

        return builder;
    }

    /// <summary>
    /// Starts the topology schedule: the loop that reads each device's neighbour protocols and
    /// routing table so that the adjacency graph stays current.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="AddNetShieldInventory{TBuilder}"/> for the reason the other three
    /// schedulers are separate: exactly one process should decide what the estate is asked to do,
    /// and the schema step registers the module on its way past without becoming a fourth thing
    /// walking five hundred devices.
    /// </remarks>
    public static TBuilder AddNetShieldTopologyScheduler<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddHostedService<TopologyScheduler>();

        return builder;
    }
}
