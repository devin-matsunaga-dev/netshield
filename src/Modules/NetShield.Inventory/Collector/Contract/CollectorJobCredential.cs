using NetShield.Contracts.Inventory;

namespace NetShield.Inventory.Collector.Contract;

/// <summary>
/// The credential a leased job is to be run with, opened.
/// </summary>
/// <remarks>
/// Internal for the same reason <see cref="CollectorCredentialMaterial"/> is, and it carries the
/// profile's id so that a collector's log line and the API's audit row name the same thing
/// without either of them naming the secret.
/// </remarks>
/// <param name="CredentialProfileId">Which profile was opened.</param>
/// <param name="Kind">Which protocol it authenticates.</param>
/// <param name="Username">The SNMP v3 security name or the SSH username.</param>
/// <param name="AuthAlgorithm">The SNMP v3 authentication algorithm.</param>
/// <param name="PrivacyAlgorithm">The SNMP v3 privacy algorithm.</param>
/// <param name="Material">The plaintext.</param>
internal sealed record CollectorJobCredential(
    Guid CredentialProfileId,
    CredentialKind Kind,
    string? Username,
    SnmpAuthAlgorithm? AuthAlgorithm,
    SnmpPrivacyAlgorithm? PrivacyAlgorithm,
    CollectorCredentialMaterial Material);
