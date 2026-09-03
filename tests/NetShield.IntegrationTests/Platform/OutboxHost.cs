using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using NetShield.Platform;
using NetShield.Platform.Messaging;
using NetShield.Platform.Persistence;

namespace NetShield.IntegrationTests.Platform;

/// <summary>
/// A host wired the way <c>NetShield.Web.Host</c> wires the outbox, against a database of its
/// own. The background dispatcher is deliberately not started: a test drives passes one at a
/// time, so what is asserted is delivery, not a timer.
/// </summary>
internal sealed class OutboxHost(IHost host) : IAsyncDisposable
{
    public static async Task<OutboxHost> StartAsync(PostgresFixture postgres, CancellationToken cancellationToken)
    {
        string connectionString = await postgres.CreateDatabaseAsync(cancellationToken);

        HostApplicationBuilder builder = Host.CreateApplicationBuilder();

        builder.Services.AddDbContext<PlatformDbContext>(options =>
            options.UseNpgsql(connectionString).UseNetShieldConventions());

        builder.AddNetShieldPlatform();

        builder.Services.AddIntegrationEvent<DeviceProbed>();
        builder.Services.AddSingleton<HandlerLog>();
        builder.Services.AddIntegrationEventHandler<DeviceProbed, RecordingHandler>();

        IHost host = builder.Build();

        // The migration is applied here, explicitly, by the thing that owns this database. The
        // running application does not migrate on startup — see STATUS.md.
        await using (AsyncServiceScope scope = host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<PlatformDbContext>()
                .Database.MigrateAsync(cancellationToken);
        }

        return new OutboxHost(host);
    }

    /// <summary>What the handler has seen.</summary>
    public HandlerLog Log => host.Services.GetRequiredService<HandlerLog>();

    /// <summary>A unit of work, as a request or a dispatch pass would have.</summary>
    public AsyncServiceScope CreateScope() => host.Services.CreateAsyncScope();

    /// <summary>Runs exactly one dispatch pass and reports how many rows it delivered.</summary>
    public async Task<int> DispatchOnceAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = CreateScope();

        return await scope.ServiceProvider.GetRequiredService<OutboxProcessor>()
            .DispatchPendingAsync(cancellationToken);
    }

    /// <summary>Every outbox row, read fresh so nothing is served from a tracker.</summary>
    public async Task<IReadOnlyList<OutboxMessage>> ReadOutboxAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = CreateScope();

        return await scope.ServiceProvider.GetRequiredService<PlatformDbContext>()
            .OutboxMessages.AsNoTracking()
            .OrderBy(message => message.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync() => await CastAndDisposeAsync(host);

    private static async ValueTask CastAndDisposeAsync(IHost disposable)
    {
        if (disposable is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
            return;
        }

        disposable.Dispose();
    }
}
