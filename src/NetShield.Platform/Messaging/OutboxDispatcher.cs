using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NetShield.Platform.Messaging;

/// <summary>
/// Runs <see cref="OutboxProcessor"/> on a loop. This is the only thing that turns a committed
/// outbox row into a delivered event.
/// </summary>
internal sealed class OutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxOptions> options,
    TimeProvider timeProvider,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        OutboxOptions settings = options.Value;
        TimeSpan delay = settings.PollInterval;
        bool failing = false;

        logger.LogInformation(
            "Outbox dispatcher started, polling every {PollInterval} in batches of {BatchSize}",
            settings.PollInterval,
            settings.BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

                int delivered = await scope.ServiceProvider
                    .GetRequiredService<OutboxProcessor>()
                    .DispatchPendingAsync(stoppingToken);

                delay = settings.PollInterval;
                failing = false;

                // A full batch means more rows were already waiting; go straight round again
                // rather than sleeping while a backlog drains.
                if (delivered >= settings.BatchSize)
                {
                    continue;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // The first failure of a run needs a human; the ones that follow it are the same
                // outage still being retried, and are logged as the degraded state they are
                // (CONVENTIONS.md §8).
                if (failing)
                {
                    logger.LogWarning(
                        "Outbox dispatch is still failing; retrying in {RetryDelay}",
                        delay);
                }
                else
                {
                    logger.LogError(exception, "Outbox dispatch failed; retrying with backoff");
                    failing = true;
                }

                delay = Backoff(delay, settings);
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

        logger.LogInformation("Outbox dispatcher stopped");
    }

    private static TimeSpan Backoff(TimeSpan current, OutboxOptions settings)
    {
        TimeSpan doubled = current * 2;

        return doubled > settings.MaxPollInterval ? settings.MaxPollInterval : doubled;
    }
}
