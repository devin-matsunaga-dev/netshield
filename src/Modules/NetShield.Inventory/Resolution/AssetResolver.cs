using System.Net;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NetShield.Contracts.Inventory;

using NetShield.Inventory.Clients;
using NetShield.Inventory.Persistence;

using NetShield.Platform.Caching;

namespace NetShield.Inventory.Resolution;

/// <summary>
/// Answers what held an address at an instant, from a Redis-cached history rebuilt from
/// PostgreSQL on a miss.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What this file is and is not.</strong> The order between the two things that can
/// claim an address is <see cref="AssetResolutionRule"/> — one pure function, tested as
/// arithmetic. What is here is the machinery around it: reading a bounded window of an address's
/// history, caching it, and reading through to the database for an instant older than the window.
/// </para>
/// <para>
/// <strong>The cache is never the source of truth</strong> (ARCHITECTURE.md §3). A miss, an
/// unreachable Redis, an entry that will not deserialise and an instant older than the cached
/// window all lead to the same place: read PostgreSQL and answer from it. A full flush costs a
/// query per address until the entries are rebuilt, and nothing else — which is what the cold
/// suite asserts by running every resolution test against the null cache as well as a real one.
/// </para>
/// </remarks>
internal sealed class AssetResolver(
    InventoryDbContext context,
    ICacheStore cache,
    IOptions<ClientOptions> options,
    ILogger<AssetResolver> logger) : IAssetResolver
{
    public async Task<AssetResolution> ResolveAtAsync(
        IPAddress address,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(address);

        DateTimeOffset instant = at.ToUniversalTime();

        AddressHistory history = await ReadAsync(address, instant, cancellationToken);

        return AssetResolutionRule.Apply(address, instant, history);
    }

    /// <summary>
    /// The address's history: from the cache when it holds one that reaches far enough back,
    /// otherwise from the database.
    /// </summary>
    /// <remarks>
    /// A history read from the database is written back only when it is the bounded recent
    /// window — the one a later read of any instant inside it can reuse. A history built for an
    /// instant older than the window is not cached, because storing it would replace the entry
    /// every ordinary resolution actually wants with one covering a moment nobody asks about
    /// twice.
    /// </remarks>
    private async Task<AddressHistory> ReadAsync(
        IPAddress address,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        string key = AssetCacheKeys.For(address);

        if (Deserialize(await cache.GetAsync(key, cancellationToken), key) is { } cached
            && cached.Covers(at))
        {
            return cached;
        }

        AddressHistory recent = await BuildRecentAsync(address, cancellationToken);

        await cache.SetAsync(
            key,
            JsonSerializer.SerializeToUtf8Bytes(recent, ResolutionSerializerContext.Default.AddressHistory),
            TimeSpan.FromSeconds(options.Value.ResolutionCacheSeconds),
            cancellationToken);

        return recent.Covers(at)
            ? recent
            : await BuildAtAsync(address, at, recent.Device, cancellationToken);
    }

    /// <summary>The bounded recent window: the device, and the newest intervals for the address.</summary>
    /// <remarks>
    /// Written out as one query rather than through a shared helper, and so is the read-through
    /// below. EF can neither order nor filter by a member of a record it has already been asked
    /// to construct, so the ordering and the projection have to sit in the same expression —
    /// factoring the join out is what makes the query untranslatable.
    /// </remarks>
    private async Task<AddressHistory> BuildRecentAsync(
        IPAddress address,
        CancellationToken cancellationToken)
    {
        CachedDevice? device = await DeviceAsync(address, cancellationToken);

        // One more than the cap, so that "there are older ones" is answered by the same query
        // rather than by a second count.
        List<CachedBinding> bindings = await (
            from binding in context.ClientIpBindings.AsNoTracking()
            join client in context.Clients.AsNoTracking() on binding.ClientId equals client.Id
            where binding.IpAddress.Equals(address)
            orderby binding.ObservedFrom descending
            select new CachedBinding(
                client.Id,
                client.MacAddress,
                binding.Source,
                binding.ObservedFrom,
                binding.ObservedTo))
            .Take(ClientLimits.CachedBindings + 1)
            .ToListAsync(cancellationToken);

        bool bounded = bindings.Count > ClientLimits.CachedBindings;

        if (bounded)
        {
            bindings.RemoveAt(bindings.Count - 1);
        }

        return new AddressHistory(
            device,
            bindings,
            bounded ? bindings[^1].ObservedFrom : null);
    }

    /// <summary>
    /// The one interval covering an instant older than the cached window, read directly.
    /// </summary>
    /// <remarks>
    /// The read-through path, and the reason a bounded cache does not become a wrong answer. It
    /// is a single indexed lookup rather than a window, because a query this far back is asking
    /// about one moment rather than browsing.
    /// </remarks>
    private async Task<AddressHistory> BuildAtAsync(
        IPAddress address,
        DateTimeOffset at,
        CachedDevice? device,
        CancellationToken cancellationToken)
    {
        List<CachedBinding> covering = await (
            from binding in context.ClientIpBindings.AsNoTracking()
            join client in context.Clients.AsNoTracking() on binding.ClientId equals client.Id
            where binding.IpAddress.Equals(address)
                && binding.ObservedFrom <= at
                && (binding.ObservedTo == null || binding.ObservedTo > at)
            select new CachedBinding(
                client.Id,
                client.MacAddress,
                binding.Source,
                binding.ObservedFrom,
                binding.ObservedTo))
            .Take(1)
            .ToListAsync(cancellationToken);

        return new AddressHistory(device, covering, Horizon: null);
    }

    private async Task<CachedDevice?> DeviceAsync(IPAddress address, CancellationToken cancellationToken) =>
        await context.Devices.AsNoTracking()
            .Where(device => device.DeletedAt == null && device.PrimaryIpAddress.Equals(address))
            .Select(device => new CachedDevice(device.Id, device.Hostname, device.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// A cached entry, or nothing when there was none or it cannot be read.
    /// </summary>
    /// <remarks>
    /// An entry written by an older build whose shape has moved deserialises to nothing, which is
    /// a miss, which rebuilds it. That is the correct behaviour for a cache and the reason every
    /// member of the document names its JSON property explicitly.
    /// </remarks>
    private AddressHistory? Deserialize(byte[]? value, string key)
    {
        if (value is null || value.Length == 0)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(value, ResolutionSerializerContext.Default.AddressHistory);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(
                exception,
                "A cached address history could not be read and will be rebuilt: {CacheKey}",
                key);

            return null;
        }
    }
}
