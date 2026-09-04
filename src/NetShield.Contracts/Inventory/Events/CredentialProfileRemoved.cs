using NetShield.Contracts.Messaging;

namespace NetShield.Contracts.Inventory.Events;

/// <summary>A credential profile was removed, and every assignment of it with it.</summary>
/// <param name="CredentialProfileId">The profile.</param>
/// <param name="Kind">Which protocol it authenticated.</param>
public sealed record CredentialProfileRemoved(Guid CredentialProfileId, CredentialKind Kind)
    : IIntegrationEvent;
