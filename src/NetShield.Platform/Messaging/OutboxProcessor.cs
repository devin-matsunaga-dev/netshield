using System.Collections.Concurrent;
using System.Reflection;

using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NetShield.Contracts.Messaging;

using NetShield.Platform.Logging;
using NetShield.Platform.Persistence;
using NetShield.Platform.Time;

namespace NetShield.Platform.Messaging;

/// <summary>
/// One pass of the outbox: claim the oldest pending rows, hand each event to its handlers, and
/// record what happened. Separated from <see cref="OutboxDispatcher"/> so that delivery can be
/// driven a pass at a time by a test instead of by a timer.
/// </summary>
internal sealed class OutboxProcessor(
    PlatformDbContext context,
    IntegrationEventRegistry registry,
    IServiceProvider services,
    IClock clock,
    SecretRedactor redactor,
    IOptions<OutboxOptions> options,
    ILogger<OutboxProcessor> logger)
{
    /// <summary>The longest a stored failure reason may be, matching the column.</summary>
    private const int MaxErrorLength = 2000;

    private static readonly ConcurrentDictionary<Type, Func<IServiceProvider, IIntegrationEvent, CancellationToken, Task>>
        s_dispatchers = new();

    /// <summary>
    /// Delivers up to one batch. Returns how many rows were delivered, which is what tells the
    /// dispatcher whether more work is already waiting.
    /// </summary>
    public async Task<int> DispatchPendingAsync(CancellationToken cancellationToken)
    {
        OutboxOptions settings = options.Value;

        List<OutboxMessage> pending = await context.OutboxMessages
            .Where(message => message.ProcessedAt == null && message.Attempts < settings.MaxAttempts)
            .OrderBy(message => message.CreatedAt)
            .ThenBy(message => message.Id)
            .Take(settings.BatchSize)
            .ToListAsync(cancellationToken);

        if (pending.Count == 0)
        {
            return 0;
        }

        int delivered = 0;

        foreach (OutboxMessage message in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();

            message.Attempts++;
            message.UpdatedAt = clock.UtcNow;

            try
            {
                await DeliverAsync(message, cancellationToken);
                message.ProcessedAt = clock.UtcNow;
                message.Error = null;
                delivered++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                message.Error = Describe(exception);

                logger.LogError(
                    exception,
                    "Outbox message {OutboxMessageId} of type {IntegrationEventType} failed on attempt {Attempt} of {MaxAttempts}",
                    message.Id,
                    message.EventType,
                    message.Attempts,
                    settings.MaxAttempts);

                if (message.Attempts >= settings.MaxAttempts)
                {
                    logger.LogError(
                        "Outbox message {OutboxMessageId} has parked after {Attempt} attempts and will not be retried",
                        message.Id,
                        message.Attempts);
                }
            }
        }

        await context.SaveChangesAsync(cancellationToken);

        return delivered;
    }

    private async Task DeliverAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        if (!registry.TryResolve(message.EventType, out Type eventType))
        {
            throw new InvalidOperationException(
                $"'{message.EventType}' is not an integration event this host carries.");
        }

        IIntegrationEvent integrationEvent = OutboxPayload.Deserialize(message.Payload, eventType)
            ?? throw new InvalidOperationException($"The payload of '{message.EventType}' deserialised to null.");

        await DispatcherFor(eventType)(services, integrationEvent, cancellationToken);
    }

    /// <summary>
    /// Redacts and truncates a failure before it is stored. SPEC.md §5 covers the database, and
    /// an exception raised while handling a credential-shaped event is exactly where a secret
    /// would otherwise land in a column.
    /// </summary>
    private string Describe(Exception exception)
    {
        string message = redactor.RedactText($"{exception.GetType().Name}: {exception.Message}");

        return message.Length <= MaxErrorLength ? message : message[..MaxErrorLength];
    }

    private static Func<IServiceProvider, IIntegrationEvent, CancellationToken, Task> DispatcherFor(Type eventType) =>
        s_dispatchers.GetOrAdd(eventType, static type => typeof(OutboxProcessor)
            .GetMethod(nameof(HandleAsync), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(type)
            .CreateDelegate<Func<IServiceProvider, IIntegrationEvent, CancellationToken, Task>>());

    /// <summary>
    /// Invokes every handler registered for one event type. An event nobody handles is still
    /// delivered — a subscriber is optional, the record of the event is not.
    /// </summary>
    private static async Task HandleAsync<TEvent>(
        IServiceProvider services,
        IIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent
    {
        foreach (IIntegrationEventHandler<TEvent> handler in services.GetServices<IIntegrationEventHandler<TEvent>>())
        {
            await handler.HandleAsync((TEvent)integrationEvent, cancellationToken);
        }
    }
}
