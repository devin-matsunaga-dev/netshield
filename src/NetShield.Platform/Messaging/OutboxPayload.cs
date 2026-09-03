using System.Text.Json;

using NetShield.Contracts.Messaging;

namespace NetShield.Platform.Messaging;

/// <summary>
/// Serialises an event into an outbox row and back. Kept in one place so that what is written
/// and what is read can never drift apart.
/// </summary>
internal static class OutboxPayload
{
    /// <summary>
    /// The wire format for stored events. It is not the API's format: these rows are internal,
    /// long-lived, and read back by this process alone, so the property names stay as the CLR
    /// type declares them rather than following the camel-case API convention.
    /// </summary>
    private static readonly JsonSerializerOptions s_options = new(JsonSerializerDefaults.General)
    {
        WriteIndented = false
    };

    internal static string Serialize(IIntegrationEvent integrationEvent) =>
        JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType(), s_options);

    internal static IIntegrationEvent? Deserialize(string payload, Type eventType) =>
        JsonSerializer.Deserialize(payload, eventType, s_options) as IIntegrationEvent;
}
