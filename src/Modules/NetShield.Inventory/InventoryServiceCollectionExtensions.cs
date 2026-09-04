using FluentValidation;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

using NetShield.Contracts.Inventory;
using NetShield.Contracts.Inventory.Events;

using NetShield.Inventory.Devices.Handlers;
using NetShield.Inventory.Endpoints;

using NetShield.Platform;

namespace NetShield.Inventory;

/// <summary>
/// Registers the Inventory module: the device handlers, their validators, and the integration
/// events this module can publish.
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

        builder.Services.TryAddScoped<GetDeviceListHandler>();
        builder.Services.TryAddScoped<GetDeviceHandler>();
        builder.Services.TryAddScoped<CreateDeviceHandler>();
        builder.Services.TryAddScoped<UpdateDeviceHandler>();
        builder.Services.TryAddScoped<DeleteDeviceHandler>();

        builder.Services.TryAddScoped<IValidator<CreateDeviceRequest>, CreateDeviceRequestValidator>();
        builder.Services.TryAddScoped<IValidator<UpdateDeviceRequest>, UpdateDeviceRequestValidator>();

        // Declared here rather than at the composition root, so that a module and the events it
        // publishes arrive together. An event the registry does not know is refused at the write
        // rather than becoming a row nothing can read back.
        builder.Services.AddIntegrationEvent<DeviceCreated>();
        builder.Services.AddIntegrationEvent<DeviceUpdated>();
        builder.Services.AddIntegrationEvent<DeviceRemoved>();

        builder.Services.ConfigureHttpJsonOptions(json =>
            json.SerializerOptions.TypeInfoResolverChain.Insert(0, InventorySerializerContext.Default));

        return builder;
    }
}
