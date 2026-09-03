using NetShield.Contracts.Messaging;

namespace NetShield.Platform.Messaging;

/// <summary>
/// Translates between an event type and the name stored against it in the outbox. Resolving a
/// stored name goes through this rather than <see cref="Type.GetType(string)"/>, so a row can
/// only ever name a type the host declared it can carry.
/// </summary>
public sealed class IntegrationEventRegistry
{
    private readonly Dictionary<string, Type> _byName;
    private readonly Dictionary<Type, string> _byType;

    public IntegrationEventRegistry(IEnumerable<IntegrationEventRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);

        _byName = [];
        _byType = [];

        foreach (IntegrationEventRegistration registration in registrations)
        {
            _byName[registration.Name] = registration.EventType;
            _byType[registration.EventType] = registration.Name;
        }
    }

    /// <summary>The stored name for an event type.</summary>
    /// <exception cref="InvalidOperationException">
    /// The type was never registered. Publishing an event the host cannot resolve again would
    /// write a row that can only ever fail, so it fails here instead, at the publish.
    /// </exception>
    public string NameOf(Type eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);

        return _byType.TryGetValue(eventType, out string? name)
            ? name
            : throw new InvalidOperationException(
                $"{eventType} is not a registered integration event. Call AddIntegrationEvent<{eventType.Name}>().");
    }

    /// <summary>Resolves a stored name back to its type, if this host carries that event.</summary>
    public bool TryResolve(string name, out Type eventType) => _byName.TryGetValue(name, out eventType!);

    /// <summary>Every event type this host can carry.</summary>
    public IReadOnlyCollection<Type> RegisteredEvents => _byType.Keys;

    /// <summary>Whether an instance may be published at all.</summary>
    public bool IsRegistered(IIntegrationEvent integrationEvent) =>
        integrationEvent is not null && _byType.ContainsKey(integrationEvent.GetType());
}
