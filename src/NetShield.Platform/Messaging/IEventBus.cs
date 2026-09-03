using NetShield.Contracts.Messaging;

namespace NetShield.Platform.Messaging;

/// <summary>
/// How a module tells the rest of the system that something happened, without knowing who is
/// listening (ARCHITECTURE.md §5).
/// </summary>
public interface IEventBus
{
    /// <summary>
    /// Enqueues an event for delivery after the caller's transaction commits.
    /// </summary>
    /// <remarks>
    /// The row is written to the same <see cref="Persistence.PlatformDbContext"/> the caller is
    /// already changing, so the domain change and the event are one atomic write: if the
    /// transaction rolls back, the event never happened, and if it commits, the event cannot be
    /// lost. Nothing is delivered at the moment of this call — the dispatcher picks it up after
    /// the commit makes it visible.
    /// </remarks>
    Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent;
}
