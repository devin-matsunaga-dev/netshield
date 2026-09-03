using NetShield.Contracts.Messaging;

namespace NetShield.Platform.Messaging;

/// <summary>
/// One event type a host has declared it can carry. Collected from DI by
/// <see cref="IntegrationEventRegistry"/>, which is what keeps the registry immutable once built.
/// </summary>
public sealed class IntegrationEventRegistration
{
    /// <param name="eventType">A concrete type implementing <see cref="IIntegrationEvent"/>.</param>
    public IntegrationEventRegistration(Type eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);

        if (!typeof(IIntegrationEvent).IsAssignableFrom(eventType))
        {
            throw new ArgumentException(
                $"{eventType} does not implement {nameof(IIntegrationEvent)} and cannot travel on the bus.",
                nameof(eventType));
        }

        EventType = eventType;
        Name = eventType.FullName
            ?? throw new ArgumentException("An open generic type cannot be an integration event.", nameof(eventType));
    }

    /// <summary>The CLR type.</summary>
    public Type EventType { get; }

    /// <summary>
    /// The name written to <c>outbox_messages.event_type</c>. It is the full type name, which
    /// makes moving or renaming an event type a breaking change for rows still in flight.
    /// </summary>
    public string Name { get; }
}
