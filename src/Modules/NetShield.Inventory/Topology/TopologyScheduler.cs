using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NetShield.Inventory.Topology;

/// <summary>
/// Runs <see cref="TopologySchedulePass"/> on a loop. This is what keeps the graph current.
/// </summary>
/// <remarks>
/// <para>
/// Registered by an opt-in call rather than by <c>AddNetShieldInventory</c>, for the reason
/// <c>ReachabilityScheduler</c>, <c>DiscoveryScheduler</c>, <c>ClientScheduler</c> and
/// <c>AddOutboxDispatcher</c> are: exactly one process in a deployment should decide what the
/// estate is asked to do, and which process that is has to be a visible choice at the composition
/// root. The schema step registers the module too, and it must not start walking neighbour tables
/// on its way through.
/// </para>
/// <para>
/// Failure is backed off rather than retried at the scan interval, with the first failure of a
/// run at <c>Error</c> and the ones that follow at <c>Warning</c> (CONVENTIONS.md §8).
/// </para>
/// </remarks>
internal sealed class TopologyScheduler(
    IServiceScopeFactory scopeFactory,
    IOptions<TopologyOptions> options,
    TimeProvider timeProvider,
    ILogger<TopologyScheduler> logger) : BackgroundService
{
    /// <summary>The longest the loop will wait between passes when it is failing.</summary>
    private static readonly TimeSpan MaxDelay = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TopologyOptions settings = options.Value;

        if (!settings.Enabled)
        {
            logger.LogInformation("Topology collection is disabled; no neighbour or route walks will be queued");

            return;
        }

        TimeSpan interval = TimeSpan.FromSeconds(settings.ScanIntervalSeconds);
        TimeSpan delay = interval;
        bool failing = false;

        logger.LogInformation(
            "Topology scheduler started, scanning every {ScanInterval}; neighbours every {WalkInterval}, routes every {RouteInterval}",
            interval,
            TimeSpan.FromSeconds(settings.NeighborWalkIntervalSeconds),
            TimeSpan.FromSeconds(settings.RouteWalkIntervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

                await scope.ServiceProvider
                    .GetRequiredService<TopologySchedulePass>()
                    .ScheduleDueAsync(stoppingToken);

                delay = interval;
                failing = false;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                if (failing)
                {
                    logger.LogWarning("Topology scheduling is still failing; retrying in {RetryDelay}", delay);
                }
                else
                {
                    logger.LogError(exception, "Topology scheduling failed; retrying with backoff");
                    failing = true;
                }

                delay = Backoff(delay);
            }

            try
            {
                await Task.Delay(delay, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("Topology scheduler stopped");
    }

    private static TimeSpan Backoff(TimeSpan current)
    {
        TimeSpan doubled = current * 2;

        return doubled > MaxDelay ? MaxDelay : doubled;
    }
}
