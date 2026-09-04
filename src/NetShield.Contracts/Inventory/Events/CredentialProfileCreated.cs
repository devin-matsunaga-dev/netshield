using NetShield.Contracts.Messaging;

namespace NetShield.Contracts.Inventory.Events;

/// <summary>A credential profile was created.</summary>
/// <remarks>
/// It carries an identifier and a classification, and nothing else. An event is a row in
/// <c>outbox_messages</c> that any module may read: SPEC.md §5 admits no credential in a
/// database column in plaintext, and a payload wide enough to be useful without a second query
/// would be a plaintext snapshot of a credential record sitting in one.
/// </remarks>
/// <param name="CredentialProfileId">The profile.</param>
/// <param name="Kind">Which protocol it authenticates.</param>
public sealed record CredentialProfileCreated(Guid CredentialProfileId, CredentialKind Kind)
    : IIntegrationEvent;
