using NetShield.Contracts.Messaging;

namespace NetShield.Contracts.Inventory.Events;

/// <summary>A credential profile changed.</summary>
/// <remarks>
/// <see cref="MaterialChanged"/> is the member a subscriber acts on. A renamed profile is a
/// display change and nothing needs to do anything about it; a profile whose material was
/// replaced is one every cached decryption of is now stale, and one whose next collection run
/// will succeed or fail differently. Whether the material changed is not itself a secret.
/// </remarks>
/// <param name="CredentialProfileId">The profile.</param>
/// <param name="Kind">Which protocol it authenticates.</param>
/// <param name="MaterialChanged">Whether the secret half was replaced, rather than a describable attribute.</param>
public sealed record CredentialProfileUpdated(
    Guid CredentialProfileId,
    CredentialKind Kind,
    bool MaterialChanged) : IIntegrationEvent;
