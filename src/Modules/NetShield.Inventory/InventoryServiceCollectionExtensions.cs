using FluentValidation;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

using NetShield.Contracts.Collector.Events;
using NetShield.Contracts.Inventory;
using NetShield.Contracts.Inventory.Events;

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

        builder.Services.TryAddScoped<GetDeviceListHandler>();
        builder.Services.TryAddScoped<GetDeviceHandler>();
        builder.Services.TryAddScoped<CreateDeviceHandler>();
        builder.Services.TryAddScoped<UpdateDeviceHandler>();
        builder.Services.TryAddScoped<DeleteDeviceHandler>();

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
        // The two subscribers each read only the jobs their own package queued: one filters on a
        // Poll naming the ICMP probe, the other on a Discover naming the SNMP walk.
        builder.Services.TryAddScoped<QueueDeviceWalkHandler>();
        builder.Services.AddScoped<IIntegrationEventHandler<CollectorJobCompleted>,
            RecordSnmpWalkResultHandler>();

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
}
