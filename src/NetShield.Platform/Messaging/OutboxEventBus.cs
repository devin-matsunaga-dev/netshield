using NetShield.Contracts.Messaging;

using NetShield.Platform.Persistence;
using NetShield.Platform.Time;

namespace NetShield.Platform.Messaging;

/// <summary>
/// The transactional half of the outbox: it writes the row and nothing else. Delivery is
/// <see cref="OutboxProcessor"/>'s job, after the caller commits.
/// </summary>
internal sealed class OutboxEventBus(
    PlatformDbContext context,
    IntegrationEventRegistry registry,
    IClock clock) : IEventBus
{
    public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        cancellationToken.ThrowIfCancellationRequested();

        DateTimeOffset now = clock.UtcNow;

        // The runtime type, not TEvent: a caller holding the event through a base type or an
        // interface must still write the row that names what it actually published.
        context.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.CreateVersion7(now),
            EventType = registry.NameOf(integrationEvent.GetType()),
            Payload = OutboxPayload.Serialize(integrationEvent),
            CreatedAt = now,
            UpdatedAt = now
        });

        // Deliberately no SaveChanges. The row belongs to the caller's transaction, and saving
        // it here would be the exact coupling the outbox exists to prevent.
        return Task.CompletedTask;
    }
}
