using NetShield.Contracts.Messaging;

using NetShield.Platform.Persistence;

namespace NetShield.Platform.Messaging;

/// <summary>
/// The transactional half of the outbox: it writes the row and nothing else. Delivery is
/// <see cref="OutboxProcessor"/>'s job, after the caller commits.
/// </summary>
internal sealed class OutboxEventBus(PlatformDbContext context, OutboxEnlistment enlistment) : IEventBus
{
    public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        cancellationToken.ThrowIfCancellationRequested();

        enlistment.Enlist(context, integrationEvent);

        // Deliberately no SaveChanges. The row belongs to the caller's transaction, and saving
        // it here would be the exact coupling the outbox exists to prevent.
        return Task.CompletedTask;
    }
}
