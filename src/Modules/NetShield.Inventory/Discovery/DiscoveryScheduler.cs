using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NetShield.Inventory.Discovery;

/// <summary>
/// Runs <see cref="DiscoverySchedulePass"/> on a loop. This is what makes a discovery seed
/// something that happens rather than something that is configured.
/// </summary>
/// <remarks>
/// <para>
/// Registered by an opt-in call rather than by <c>AddNetShieldInventory</c>, for the reason
/// <c>ReachabilityScheduler</c> and <c>AddOutboxDispatcher</c> are: exactly one process in a
/// deployment should decide what the estate is asked to do, and a sweep of the estate's address
/// space is the most conspicuous thing NetShield does on its own. The schema step registers the
/// module on its way past and must not start sweeping.
/// </para>
/// <para>
/// Failure is backed off rather than retried at the scan interval, and the first failure of a
/// run is logged at <c>Error</c> while the ones that follow are logged at <c>Warning</c>
/// (CONVENTIONS.md §8).
/// </para>
/// </remarks>
internal sealed class DiscoveryScheduler(
    IServiceScopeFactory scopeFactory,
    IOptions<DiscoveryOptions> options,
    TimeProvider timeProvider,
    ILogger<DiscoveryScheduler> logger) : BackgroundService
{
    /// <summary>The longest the loop will wait between passes when it is failing.</summary>
    private static readonly TimeSpan MaxDelay = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        DiscoveryOptions settings = options.Value;

        if (!settings.ScheduleEnabled)
        {
            logger.LogInformation("Discovery scheduling is disabled; no runs will be started");

            return;
        }

        TimeSpan interval = TimeSpan.FromSeconds(settings.ScanIntervalSeconds);
        TimeSpan delay = interval;
        bool failing = false;

        logger.LogInformation(
            "Discovery scheduler started, scanning every {ScanInterval} for seeds that have fallen due",
            interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

                await scope.ServiceProvider
                    .GetRequiredService<DiscoverySchedulePass>()
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
                    logger.LogWarning(
                        "Discovery scheduling is still failing; retrying in {RetryDelay}",
                        delay);
                }
                else
                {
                    logger.LogError(exception, "Discovery scheduling failed; retrying with backoff");
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

        logger.LogInformation("Discovery scheduler stopped");
    }

    private static TimeSpan Backoff(TimeSpan current)
    {
        TimeSpan doubled = current * 2;

        return doubled > MaxDelay ? MaxDelay : doubled;
    }
}
