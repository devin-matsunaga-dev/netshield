namespace NetShield.Contracts.Inventory;

/// <summary>
/// What a caller supplies to create a credential profile.
/// </summary>
/// <remarks>
/// <see cref="Kind"/> is set here and never again. It decides which members of
/// <see cref="Material"/> are required, and a profile whose kind changed would hold material
/// describing a protocol it no longer claims to be for.
/// </remarks>
/// <param name="Name">What an operator calls it. Required; unique among live profiles.</param>
/// <param name="Kind">Which protocol it authenticates. Required, and immutable afterwards.</param>
/// <param name="Material">The secret half. Required, and never returned by anything.</param>
/// <param name="Description">What it is for.</param>
/// <param name="Username">The SNMP v3 security name or the SSH username. Required for every kind but SNMP v2c.</param>
/// <param name="AuthAlgorithm">The SNMP v3 authentication algorithm. Required for SNMP v3, refused otherwise.</param>
/// <param name="PrivacyAlgorithm">The SNMP v3 privacy algorithm. Required for SNMP v3, refused otherwise.</param>
public sealed record CreateCredentialProfileRequest(
    string Name,
    CredentialKind Kind,
    CredentialMaterial Material,
    string? Description = null,
    string? Username = null,
    SnmpAuthAlgorithm? AuthAlgorithm = null,
    SnmpPrivacyAlgorithm? PrivacyAlgorithm = null);
