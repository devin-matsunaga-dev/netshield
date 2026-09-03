using NetShield.Contracts.Messaging;

namespace NetShield.UnitTests.Platform;

/// <summary>A stand-in integration event, so the registry can be tested without a module.</summary>
public sealed record ProbeEvent(string Hostname) : IIntegrationEvent;
