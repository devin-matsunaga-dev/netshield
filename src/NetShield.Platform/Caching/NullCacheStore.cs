namespace NetShield.Platform.Caching;

/// <summary>
/// A cache that remembers nothing.
/// </summary>
/// <remarks>
/// <para>
/// What a host gets when no Redis connection has been registered — the schema step, a test that
/// is not about caching, and a deployment an operator has deliberately run without one. Every
/// read is a miss and every write is discarded, which is exactly the behaviour ARCHITECTURE.md §3
/// already requires every caller to be correct under: "the system must survive a full Redis
/// flush with nothing worse than a cold cache" is the same sentence as "the system must work
/// with this implementation, only slower".
/// </para>
/// <para>
/// It is therefore not a stub. It is the proof that the rule holds, and the integration suite
/// runs the resolver against both this and a real Redis so that the two cannot come to disagree
/// about what an answer is.
/// </para>
/// </remarks>
public sealed class NullCacheStore : ICacheStore
{
    public Task<byte[]?> GetAsync(string key, CancellationToken cancellationToken) =>
        Task.FromResult<byte[]?>(null);

    public Task SetAsync(
        string key,
        byte[] value,
        TimeSpan lifetime,
        CancellationToken cancellationToken) => Task.CompletedTask;

    public Task RemoveAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
