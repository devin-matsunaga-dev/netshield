using Microsoft.EntityFrameworkCore;

using NetShield.Contracts.Messaging;

using NetShield.Platform.Time;

namespace NetShield.Platform.Messaging;

/// <summary>
/// Adds an outbox row to whichever <see cref="DbContext"/> the caller is already changing, so
/// that the domain write and the event are one transaction (ARCHITECTURE.md §5).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IEventBus"/> writes into <c>PlatformDbContext</c>, which is the right thing for
/// platform-owned writes and the wrong thing for a module: a module keeps its own context, and
/// two contexts are two connections, so "same transaction" would stop being true exactly where
/// it matters most. A module therefore maps <c>outbox_messages</c> on its own context — the
/// mapping is public for this reason, and the module excludes the table from its own migrations
/// — and enlists the row through this.
/// </para>
/// <para>
/// It does not save. The row belongs to the caller's <c>SaveChangesAsync</c>, and saving here
/// would be the exact coupling the outbox exists to prevent.
/// </para>
/// </remarks>
public sealed class OutboxEnlistment(IntegrationEventRegistry registry, IClock clock)
{
    /// <summary>
    /// Stages <paramref name="integrationEvent"/> for delivery after
    /// <paramref name="context"/> commits.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The event type was never declared with <c>AddIntegrationEvent</c>, so nothing could read
    /// the row back. Failing at the write is what keeps an undeliverable row out of the table.
    /// </exception>
    public void Enlist(DbContext context, IIntegrationEvent integrationEvent)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(integrationEvent);

        DateTimeOffset now = clock.UtcNow;

        // The runtime type, not a generic parameter: a caller holding the event through an
        // interface must still write the row that names what it actually published.
        context.Add(new OutboxMessage
        {
            Id = Guid.CreateVersion7(now),
            EventType = registry.NameOf(integrationEvent.GetType()),
            Payload = OutboxPayload.Serialize(integrationEvent),
            CreatedAt = now,
            UpdatedAt = now
        });
    }
}
