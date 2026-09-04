namespace NetShield.Contracts.Inventory;

/// <summary>
/// The secret half of a credential profile, on its way in. Every member is optional here and
/// which of them are required is decided by the profile's <see cref="CredentialKind"/>, because
/// a shape cannot express "exactly these four, but only for SNMP v3".
/// </summary>
/// <remarks>
/// <para>
/// This type travels in one direction only. It appears on a request and on no response, it is
/// encrypted before it reaches a database column, and the module's own plaintext shape is
/// internal and absent from every serializer context the API uses. That is what "write-only over
/// the API" means in WP-1.2, and three tests hold it: a walk of every response schema in the
/// OpenAPI document, a check that the module's plaintext type is not serializable by the
/// inventory context, and an integration test reading the stored bytes back with the key ring
/// switched off.
/// </para>
/// <para>
/// Every member's name is one <c>SecretRedactor</c> already recognises, so a value that reaches a
/// log or an audit snapshot through any route is blanked before it is written.
/// </para>
/// </remarks>
/// <param name="Community">The SNMP v2c community string.</param>
/// <param name="AuthPassword">The SNMP v3 authentication pass phrase.</param>
/// <param name="PrivacyPassword">The SNMP v3 privacy pass phrase, when privacy is not None.</param>
/// <param name="Password">The SSH password.</param>
/// <param name="PrivateKey">The SSH private key, in PEM form.</param>
/// <param name="PrivateKeyPassword">The pass phrase protecting the private key, when it has one.</param>
public sealed record CredentialMaterial(
    string? Community = null,
    string? AuthPassword = null,
    string? PrivacyPassword = null,
    string? Password = null,
    string? PrivateKey = null,
    string? PrivateKeyPassword = null);
