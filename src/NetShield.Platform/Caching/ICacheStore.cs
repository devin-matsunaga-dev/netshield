namespace NetShield.Platform.Caching;

/// <summary>
/// A distributed cache, narrowed to what NetShield actually asks of one.
/// </summary>
/// <remarks>
/// <para>
/// ARCHITECTURE.md §3 gives Redis one job and one rule: it is a cache, a rate limiter, a job
/// coordinator and the SignalR backplane, and it is <em>never</em> a source of truth — the system
/// must survive a full flush with nothing worse than a cold cache. This interface is shaped by
/// that rule. There is no "get or throw", no way to ask whether a key exists without reading it,
/// and no bulk operation: every caller has to be written so that a miss is ordinary, because in
/// production a miss will be.
/// </para>
/// <para>
/// It lives in <c>NetShield.Platform</c> because ARCHITECTURE.md §4 puts cross-cutting
/// infrastructure there and lets nothing reference the composition root, which is the only place
/// that knows where Redis is. A module names this interface; the host decides what is behind it.
/// </para>
/// <para>
/// <strong>A cache failure is not a request failure.</strong> Every implementation swallows a
/// transport fault and answers as a miss on a read, or as a no-op on a write. A Redis that is
/// down must slow NetShield down, not stop it — which is the same reasoning that makes the null
/// implementation a legitimate configuration rather than a degraded one.
/// </para>
/// </remarks>
public interface ICacheStore
{
    /// <summary>
    /// The bytes stored under <paramref name="key"/>, or <see langword="null"/> for a miss — an
    /// absent key, an expired one, or a cache that could not be reached.
    /// </summary>
    Task<byte[]?> GetAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// Stores <paramref name="value"/> under <paramref name="key"/> for
    /// <paramref name="lifetime"/>, replacing whatever was there.
    /// </summary>
    /// <remarks>
    /// A lifetime is required rather than optional. A cache entry with no expiry outlives every
    /// invalidation anybody forgot to write, and the failure mode is a resolution that is quietly
    /// wrong for ever.
    /// </remarks>
    Task SetAsync(string key, byte[] value, TimeSpan lifetime, CancellationToken cancellationToken);

    /// <summary>Drops <paramref name="keys"/>. Keys that are not there are not an error.</summary>
    Task RemoveAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken);
}
