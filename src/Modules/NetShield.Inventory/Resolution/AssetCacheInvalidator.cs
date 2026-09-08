using Microsoft.Extensions.Logging;

using NetShield.Contracts.Inventory.Events;

using NetShield.Platform.Caching;
using NetShield.Platform.Messaging;

namespace NetShield.Inventory.Resolution;

/// <summary>
/// Drops a cached address history when the inventory changes what an address means.
/// </summary>
/// <remarks>
/// <para>
/// The "invalidated on inventory change" half of WP-1.8. A cached history carries the device
/// holding the address as well as the client intervals for it, so a device arriving at an
/// address, leaving one, or moving between two makes the entries for those addresses wrong — and
/// wrong in the direction that matters, since a resolution that still names a removed device is
/// an attribution nobody would think to question.
/// </para>
/// <para>
/// <strong>An update invalidates two keys.</strong> <c>DeviceUpdated</c> carries the previous
/// address beside the current one, written into the contract by WP-1.1 with the note that the
/// address resolution in WP-1.8 would be the first thing to need it — because a subscriber
/// holding only the new address has no way to name the entry the device has just vacated. When
/// the address did not change the two are equal and the set collapses to one key.
/// </para>
/// <para>
/// The bindings side of invalidation is not here. A walk that opens or closes a binding drops
/// that address's key inside the same handler that wrote it, because the write and the
/// invalidation belong to one act and a subscriber would run after the commit rather than with
/// it.
/// </para>
/// </remarks>
internal sealed class AssetCacheInvalidator(ICacheStore cache, ILogger<AssetCacheInvalidator> logger) :
    IIntegrationEventHandler<DeviceCreated>,
    IIntegrationEventHandler<DeviceUpdated>,
    IIntegrationEventHandler<DeviceRemoved>
{
    public Task HandleAsync(DeviceCreated integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        return InvalidateAsync(cancellationToken, integrationEvent.PrimaryIpAddress);
    }

    public Task HandleAsync(DeviceUpdated integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        return InvalidateAsync(
            cancellationToken,
            integrationEvent.PrimaryIpAddress,
            integrationEvent.PreviousPrimaryIpAddress);
    }

    public Task HandleAsync(DeviceRemoved integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        return InvalidateAsync(cancellationToken, integrationEvent.PrimaryIpAddress);
    }

    private async Task InvalidateAsync(CancellationToken cancellationToken, params string?[] addresses)
    {
        // A set, because an update that did not move the address names it twice. An address that
        // will not parse is one no key was ever written under.
        HashSet<string> keys = new(StringComparer.Ordinal);

        foreach (string? address in addresses)
        {
            if (AssetCacheKeys.For(address) is { } key)
            {
                keys.Add(key);
            }
        }

        if (keys.Count == 0)
        {
            return;
        }

        await cache.RemoveAsync(keys, cancellationToken);

        logger.LogDebug("Invalidated {Count} cached address histories after an inventory change", keys.Count);
    }
}
