using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NetShield.Inventory.Reachability;

/// <summary>
/// Runs <see cref="ReachabilitySchedulePass"/> on a loop. This is what actually keeps the
/// collector queue fed.
/// </summary>
/// <remarks>
/// <para>
/// Registered by an opt-in call rather than by <c>AddNetShieldInventory</c>, for the reason
/// <c>AddOutboxDispatcher</c> is: exactly one process in a deployment should decide what the
/// estate is asked to do, and which process that is has to be a visible choice at the composition
/// root rather than a consequence of having registered a module. The schema step registers the
/// module too, and it must not start scheduling probes on its way through.
/// </para>
/// <para>
/// Failure is backed off rather than retried at the scan interval, and the first failure of a run
/// is logged at <c>Error</c> while the ones that follow are logged at <c>Warning</c>
/// (CONVENTIONS.md §8) — an unreachable database would otherwise produce one error every fifteen
/// seconds for as long as the outage lasted.
/// </para>
/// </remarks>
internal sealed class ReachabilityScheduler(
    IServiceScopeFactory scopeFactory,
    IOptions<ReachabilityOptions> options,
    TimeProvider timeProvider,
    ILogger<ReachabilityScheduler> logger) : BackgroundService
{
    /// <summary>The longest the loop will wait between passes when it is failing.</summary>
    private static readonly TimeSpan MaxDelay = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ReachabilityOptions settings = options.Value;

        if (!settings.Enabled)
        {
            logger.LogInformation("Reachability scheduling is disabled; no probes will be queued");

            return;
        }

        TimeSpan interval = TimeSpan.FromSeconds(settings.ScanIntervalSeconds);
        TimeSpan delay = interval;
        bool failing = false;

        logger.LogInformation(
            "Reachability scheduler started, scanning every {ScanInterval} for devices due on a {PollInterval} interval",
            interval,
            TimeSpan.FromSeconds(settings.PollIntervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

                await scope.ServiceProvider
                    .GetRequiredService<ReachabilitySchedulePass>()
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
                        "Reachability scheduling is still failing; retrying in {RetryDelay}",
                        delay);
                }
                else
                {
                    logger.LogError(exception, "Reachability scheduling failed; retrying with backoff");
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

        logger.LogInformation("Reachability scheduler stopped");
    }

    private static TimeSpan Backoff(TimeSpan current)
    {
        TimeSpan doubled = current * 2;

        return doubled > MaxDelay ? MaxDelay : doubled;
    }
}
