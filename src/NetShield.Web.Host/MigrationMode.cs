using Microsoft.EntityFrameworkCore;

using NetShield.Identity;
using NetShield.Identity.Persistence;

using NetShield.Inventory.Persistence;

using NetShield.Platform;
using NetShield.Platform.Persistence;

namespace NetShield.Web.Host;

/// <summary>
/// The schema step, run as <c>NetShield.Web.Host --migrate</c>: apply every context's migrations,
/// bootstrap the first-run administrator, and exit.
/// </summary>
/// <remarks>
/// <para>
/// It is deliberately not a startup hook. Migrating on boot means every replica races to migrate,
/// and it means the account the API runs as needs DDL rights it should not hold for the rest of
/// its life. It is equally deliberately not a sixth project: ARCHITECTURE.md §2 fixes the process
/// model at five, and a separate deployable would be a change to that model rather than to a
/// deployment step. The same artifact, run with an argument, gives the operational boundary
/// without inventing a process.
/// </para>
/// <para>
/// Nothing here binds a socket. It is a generic host, not a web host, so a migration run cannot
/// start answering requests against a schema it is halfway through changing. In Aspire the
/// migrator is a one-shot resource the API waits for; a Docker Compose deployment runs the same
/// image with the same argument before starting the API, and an EF migration bundle could later
/// replace what is behind this step without changing its shape.
/// </para>
/// </remarks>
internal static class MigrationMode
{
    /// <summary>The argument that selects this mode.</summary>
    internal const string Switch = "--migrate";

    /// <summary>Whether the process was asked to migrate rather than to serve.</summary>
    internal static bool IsRequested(string[] args) =>
        args.Contains(Switch, StringComparer.Ordinal);

    /// <summary>
    /// Applies every context's migrations, then starts the host just long enough for the
    /// first-run seeder to run, then stops.
    /// </summary>
    internal static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        // The switch itself is removed before configuration sees it: the command-line provider
        // rejects a bare `--migrate`, which has no value to bind.
        // Fully qualified: this file's own namespace ends in `Host`, which otherwise wins.
        HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(
            args.Where(argument => !string.Equals(argument, Switch, StringComparison.Ordinal)).ToArray());

        builder.AddServiceDefaults();

        // Health checks are switched off on all three. This process answers no probe, and a
        // readiness check on a connection it is about to run DDL over reports on nothing.
        builder.AddNpgsqlDbContext<PlatformDbContext>(
            ConnectionNames.Database,
            settings => settings.DisableHealthChecks = true,
            options => options.UseNetShieldConventions());

        builder.AddNpgsqlDbContext<IdentityDbContext>(
            ConnectionNames.Database,
            settings => settings.DisableHealthChecks = true,
            options => options.UseIdentityConventions());

        builder.AddNpgsqlDbContext<InventoryDbContext>(
            ConnectionNames.Database,
            settings => settings.DisableHealthChecks = true,
            options => options.UseInventoryConventions());

        builder.AddNetShieldPlatform();

        // Identity is registered for one reason: it owns the first-run administrator seeder, and
        // an empty database that has been migrated but not seeded still has nobody who can sign
        // in. No outbox dispatcher is registered — delivery is the API's job, and a process that
        // is about to exit would only claim rows and drop them.
        builder.AddNetShieldIdentity();

        using IHost host = builder.Build();

        ILogger logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(MigrationMode));

        await MigrateAsync(host.Services, logger, cancellationToken);

        // Starting the host is what runs the seeder. Stopping it immediately is what keeps this
        // a step rather than a service.
        await host.StartAsync(cancellationToken);
        await host.StopAsync(cancellationToken);

        logger.LogInformation("Migration run complete.");

        return 0;
    }

    private static async Task MigrateAsync(
        IServiceProvider services,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();

        // Platform first. It owns outbox_messages, which the Inventory context maps and excludes
        // from its own migrations — so the table has to exist before a device write can enlist a
        // row in it. The other two are independent of each other.
        await ApplyAsync<PlatformDbContext>(scope, logger, cancellationToken);
        await ApplyAsync<IdentityDbContext>(scope, logger, cancellationToken);
        await ApplyAsync<InventoryDbContext>(scope, logger, cancellationToken);
    }

    private static async Task ApplyAsync<TContext>(
        AsyncServiceScope scope,
        ILogger logger,
        CancellationToken cancellationToken)
        where TContext : DbContext
    {
        TContext context = scope.ServiceProvider.GetRequiredService<TContext>();

        IReadOnlyList<string> pending =
            [.. await context.Database.GetPendingMigrationsAsync(cancellationToken)];

        if (pending.Count == 0)
        {
            logger.LogInformation("{Context} is up to date.", typeof(TContext).Name);

            return;
        }

        logger.LogInformation(
            "Applying {Count} migration(s) to {Context}.",
            pending.Count,
            typeof(TContext).Name);

        await context.Database.MigrateAsync(cancellationToken);
    }
}
