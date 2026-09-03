using Npgsql;

using Testcontainers.PostgreSql;

namespace NetShield.IntegrationTests.Platform;

/// <summary>
/// One PostgreSQL container, shared by the tests in a class, on the same image
/// <c>NetShield.AppHost</c> runs. CONVENTIONS.md §7 admits no in-memory provider: a migration,
/// a <c>jsonb</c> column, a filtered index and a rolled-back transaction all behave like
/// themselves only on the real thing.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    /// <summary>Matches the image pinned in <c>NetShield.AppHost</c>, so tests and dev agree.</summary>
    private const string Image = "timescale/timescaledb:2.29.0-pg17";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(Image)
        .WithEnvironment("TIMESCALEDB_TELEMETRY", "off")
        .Build();

    public async ValueTask InitializeAsync() => await _container.StartAsync(TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    /// <summary>
    /// Creates an empty database and returns a connection string for it. A test that owns its own
    /// database cannot be affected by the order the others ran in, and the outbox is a table every
    /// one of these tests writes to.
    /// </summary>
    public async Task<string> CreateDatabaseAsync(CancellationToken cancellationToken)
    {
        string database = $"netshield_{Guid.CreateVersion7():N}";

        await using NpgsqlConnection connection = new(_container.GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        // The name is a generated identifier, not caller input, and CREATE DATABASE takes no
        // parameter placeholder.
        await using (NpgsqlCommand create = new($"CREATE DATABASE \"{database}\"", connection))
        {
            await create.ExecuteNonQueryAsync(cancellationToken);
        }

        return new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = database
        }.ConnectionString;
    }
}
