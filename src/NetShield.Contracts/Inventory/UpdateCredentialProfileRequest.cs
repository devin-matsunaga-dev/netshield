namespace NetShield.Contracts.Inventory;

/// <summary>
/// What a caller supplies to replace a credential profile's describable attributes.
/// </summary>
/// <remarks>
/// <para>
/// There is no material member, and there is no kind member. WP-1.1 settled that an update is
/// whole-resource replacement, and a secret that is never returned cannot be round-tripped
/// through one — a caller who GET the profile and PUT it back would have nothing to send, and
/// the obvious reading of an absent value would erase the credential. The material is replaced
/// through <c>PUT /api/v1/credential-profiles/{id}/material</c> instead, which is a request
/// about the secret and only about the secret.
/// </para>
/// <para>
/// Whole-resource replacement still applies to what is here: an omitted optional member clears
/// the stored value rather than leaving it alone.
/// </para>
/// </remarks>
/// <param name="Name">What an operator calls it. Required; unique among live profiles.</param>
/// <param name="Description">What it is for.</param>
/// <param name="Username">The SNMP v3 security name or the SSH username.</param>
/// <param name="AuthAlgorithm">The SNMP v3 authentication algorithm.</param>
/// <param name="PrivacyAlgorithm">The SNMP v3 privacy algorithm.</param>
public sealed record UpdateCredentialProfileRequest(
    string Name,
    string? Description = null,
    string? Username = null,
    SnmpAuthAlgorithm? AuthAlgorithm = null,
    SnmpPrivacyAlgorithm? PrivacyAlgorithm = null);
