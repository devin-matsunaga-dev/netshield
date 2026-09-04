using System.Text.Json.Serialization;

using NetShield.Contracts.Inventory;
using NetShield.Contracts.Paging;

namespace NetShield.Inventory.Endpoints;

/// <summary>
/// The source-generated serialiser for the inventory contract (CONVENTIONS.md §4).
/// </summary>
/// <remarks>
/// It lists the request and response shapes and nothing else. The entity is absent because it is
/// internal and has no path to a response; every enum is written as its name rather than its
/// ordinal, so inserting a member cannot change what a stored response already means.
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    Converters = [typeof(JsonStringEnumConverter<DeviceVendor>),
        typeof(JsonStringEnumConverter<DeviceRole>),
        typeof(JsonStringEnumConverter<DeviceState>),
        typeof(JsonStringEnumConverter<CriticalityTier>),
        typeof(JsonStringEnumConverter<DeviceEnvironment>)])]
[JsonSerializable(typeof(CreateDeviceRequest))]
[JsonSerializable(typeof(UpdateDeviceRequest))]
[JsonSerializable(typeof(DeviceDetail))]
[JsonSerializable(typeof(DeviceSummary))]
[JsonSerializable(typeof(CursorPage<DeviceSummary>))]
public sealed partial class InventorySerializerContext : JsonSerializerContext;
