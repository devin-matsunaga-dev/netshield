using FluentValidation;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

using NetShield.Contracts.Inventory;
using NetShield.Contracts.Inventory.Events;

using NetShield.Inventory.Credentials;
using NetShield.Inventory.Credentials.Handlers;
using NetShield.Inventory.Devices.Handlers;
using NetShield.Inventory.Endpoints;

using NetShield.Platform;

namespace NetShield.Inventory;

/// <summary>
/// Registers the Inventory module: the device and credential handlers, their validators, the
/// envelope encryption a credential is sealed with, and the integration events this module can
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

        // The decrypt path. Registered so WP-1.3 can take a dependency on it; nothing in this
        // package's HTTP surface resolves one, and the interface is internal to this module so
        // nothing outside it can name the type to ask for.
        builder.Services.TryAddScoped<ICredentialResolver, CredentialResolver>();

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

        builder.Services.ConfigureHttpJsonOptions(json =>
            json.SerializerOptions.TypeInfoResolverChain.Insert(0, InventorySerializerContext.Default));

        return builder;
    }
}
