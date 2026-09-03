using NetShield.Contracts.Messaging;

namespace NetShield.IntegrationTests.Platform;

/// <summary>An event no host declares, used to prove that publishing one is refused.</summary>
public sealed record UnregisteredEvent : IIntegrationEvent;
