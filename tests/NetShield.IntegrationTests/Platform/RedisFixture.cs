using StackExchange.Redis;

using Testcontainers.Redis;

namespace NetShield.IntegrationTests.Platform;

/// <summary>
/// One Redis container, shared by the tests in a class.
/// </summary>
/// <remarks>
/// <para>
/// CONVENTIONS.md §7 admits no in-memory provider for the store whose behaviour is the point, and
/// for WP-1.8 the cache <em>is</em> the point of two of its three criteria: "resolution is under
/// 1 ms warm" is a claim about a real round trip, and "a cold cache rebuilds from PostgreSQL
/// without a gap" is a claim about what happens when a real cache is flushed. A dictionary
/// pretending to be Redis would answer both in nanoseconds and prove nothing.
/// </para>
/// <para>
/// The image is pinned here, unlike the AppHost's Redis resource which floats on the Aspire
/// default: a suite whose container changed underneath it would fail for a reason nobody
/// touched. <c>8.6</c> is what that default resolves to today, so the two agree — and if Aspire
/// moves ahead, the suite still runs against a Redis somebody chose rather than one it happened
/// to get.
/// </para>
/// </remarks>
public sealed class RedisFixture : IAsyncLifetime
{
    /// <summary>Matches what the AppHost's floating Redis resource resolves to today.</summary>
    private const string Image = "redis:8.6";

    private readonly RedisContainer _container = new RedisBuilder(Image).Build();

    public async ValueTask InitializeAsync() =>
        await _container.StartAsync(TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    /// <summary>How a host reaches this container.</summary>
    public string ConnectionString => _container.GetConnectionString();

    /// <summary>
    /// Empties the cache, which is what a deployment's Redis restarting does.
    /// </summary>
    /// <remarks>
    /// ARCHITECTURE.md §3 says the system must survive a full flush with nothing worse than a
    /// cold cache. This is that flush, so a test can assert it rather than assume it.
    /// </remarks>
    public async Task FlushAsync()
    {
        await using ConnectionMultiplexer connection =
            await ConnectionMultiplexer.ConnectAsync($"{ConnectionString},allowAdmin=true");

        foreach (System.Net.EndPoint endpoint in connection.GetEndPoints())
        {
            await connection.GetServer(endpoint).FlushDatabaseAsync();
        }
    }

    /// <summary>How many entries the cache is holding, so a test can see it warm and see it cold.</summary>
    public async Task<long> KeyCountAsync()
    {
        await using ConnectionMultiplexer connection =
            await ConnectionMultiplexer.ConnectAsync($"{ConnectionString},allowAdmin=true");

        long count = 0;

        foreach (System.Net.EndPoint endpoint in connection.GetEndPoints())
        {
            count += connection.GetServer(endpoint).Keys(pattern: "netshield:asset:*").LongCount();
        }

        return count;
    }
}
