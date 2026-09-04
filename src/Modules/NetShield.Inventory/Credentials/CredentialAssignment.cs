using NetShield.Contracts.Inventory;

namespace NetShield.Inventory.Credentials;

/// <summary>
/// A credential a device may be reached with, named but not opened.
/// </summary>
/// <param name="CredentialProfileId">The profile.</param>
/// <param name="Kind">Which protocol it authenticates — which is what a caller picks on.</param>
/// <param name="Name">What an operator calls it, for a log line that a person has to read.</param>
internal sealed record CredentialAssignment(Guid CredentialProfileId, CredentialKind Kind, string Name);
