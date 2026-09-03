using NetShield.Contracts.Messaging;

namespace NetShield.IntegrationTests.Platform;

/// <summary>
/// A stand-in for the events Phase 1 will publish. It exists so that the outbox can be tested
/// without waiting for a real one, and it is shaped like the real ones: a record of Contracts
/// types, carrying identifiers rather than entities (ARCHITECTURE.md §4).
/// </summary>
public sealed record DeviceProbed(Guid DeviceId, string Hostname) : IIntegrationEvent;
