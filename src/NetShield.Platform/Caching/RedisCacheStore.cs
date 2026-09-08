using Microsoft.Extensions.Logging;

using StackExchange.Redis;

namespace NetShield.Platform.Caching;

/// <summary>
/// <see cref="ICacheStore"/> over the Redis connection the composition root registers.
/// </summary>
/// <remarks>
/// <para>
/// It takes <see cref="IConnectionMultiplexer"/> rather than opening its own connection, because
/// where Redis is belongs to the composition root and nowhere else (SPEC.md §5) — Aspire supplies
/// it, and no host, port or credential appears in this project.
/// </para>
/// <para>
/// <strong>Every operation swallows a transport fault.</strong> A read that could not reach Redis
/// answers as a miss and a write that could not reach it is discarded, both with a
/// <c>Warning</c>: degraded and self-healing is exactly what CONVENTIONS.md §8 reserves that
/// level for, and an outage that turned every enriched flow record into a failed write would be a
/// cache deciding whether the system runs. Anything the caller does after a miss is the same
/// thing it does on a cold cache, which it must already handle.
/// </para>
/// </remarks>
public sealed class RedisCacheStore(
    IConnectionMultiplexer connection,
    ILogger<RedisCacheStore> logger) : ICacheStore
{
    public async Task<byte[]?> GetAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            RedisValue value = await connection.GetDatabase().StringGetAsync(key);

            return value.IsNullOrEmpty ? null : (byte[]?)value;
        }
        catch (RedisException exception)
        {
            logger.LogWarning(exception, "The cache could not be read; answering as a miss");

            return null;
        }
    }

    public async Task SetAsync(
        string key,
        byte[] value,
        TimeSpan lifetime,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(value);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await connection.GetDatabase().StringSetAsync(key, value, lifetime);
        }
        catch (RedisException exception)
        {
            logger.LogWarning(exception, "The cache could not be written; the entry was dropped");
        }
    }

    public async Task RemoveAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(keys);
        cancellationToken.ThrowIfCancellationRequested();

        if (keys.Count == 0)
        {
            return;
        }

        try
        {
            await connection.GetDatabase().KeyDeleteAsync([.. keys.Select(key => (RedisKey)key)]);
        }
        catch (RedisException exception)
        {
            // The dangerous direction. A dropped invalidation leaves a stale entry standing until
            // it expires, which is why every entry has a lifetime and none is written without one.
            logger.LogWarning(
                exception,
                "The cache could not be invalidated for {Count} keys; they will expire instead",
                keys.Count);
        }
    }
}
