using NetShield.Contracts.Inventory;

namespace NetShield.Inventory.Credentials;

/// <summary>
/// One credential profile with its material opened: everything needed to authenticate to a
/// device, in memory, for as long as the caller holds it.
/// </summary>
/// <remarks>
/// Internal, and the reason it must stay internal is <see cref="Material"/>. ARCHITECTURE.md §7
/// says plaintext credentials are decrypted in the API, delivered over TLS, held in memory only,
/// and never written to collector disk or logs — and a type that cannot be named outside this
/// module cannot be held by anything that has not been written to those rules.
/// </remarks>
/// <param name="CredentialProfileId">The profile this came from.</param>
/// <param name="Kind">Which protocol it authenticates.</param>
/// <param name="Username">The SNMP v3 security name or the SSH username.</param>
/// <param name="AuthAlgorithm">The SNMP v3 authentication algorithm.</param>
/// <param name="PrivacyAlgorithm">The SNMP v3 privacy algorithm.</param>
/// <param name="Material">The plaintext material.</param>
internal sealed record ResolvedCredential(
    Guid CredentialProfileId,
    CredentialKind Kind,
    string? Username,
    SnmpAuthAlgorithm? AuthAlgorithm,
    SnmpPrivacyAlgorithm? PrivacyAlgorithm,
    CredentialMaterialPayload Material);
