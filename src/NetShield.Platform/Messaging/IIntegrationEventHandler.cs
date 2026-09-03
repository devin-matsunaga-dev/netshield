using NetShield.Contracts.Messaging;

namespace NetShield.Platform.Messaging;

/// <summary>
/// Handles an event another module raised. Registered in DI; the dispatcher resolves every
/// handler for an event type and invokes each one.
/// </summary>
/// <typeparam name="TEvent">The event this handler consumes.</typeparam>
public interface IIntegrationEventHandler<in TEvent>
    where TEvent : IIntegrationEvent
{
    /// <summary>
    /// Reacts to the event. Throwing leaves the outbox row unprocessed for a later attempt, so
    /// a handler must be safe to run twice: delivery is at-least-once, not exactly-once.
    /// </summary>
    Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken);
}
