namespace NetShield.Contracts.Inventory;

/// <summary>
/// Everything the API will say about one credential profile — which is everything except the
/// material.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="MaterialUpdatedAt"/> is the whole of what a reader learns about the secret: when it
/// last changed. That is enough to answer "has this been rotated since the incident" without the
/// API being a place a credential can be read out of.
/// </para>
/// <para>
/// Members are named so that nothing here trips <c>SecretRedactor</c>'s name rule — the WP-0.5
/// lesson, from the other side. A member called <c>secretUpdatedAt</c> would be blanked in every
/// audit snapshot and every log line that carried it, and would say nothing.
/// </para>
/// </remarks>
/// <param name="Id">The profile.</param>
/// <param name="Name">What an operator calls it. Unique among live profiles, case-insensitively.</param>
/// <param name="Description">What it is for, as free text.</param>
/// <param name="Kind">Which protocol it authenticates. Fixed at creation.</param>
/// <param name="Username">The SNMP v3 security name or the SSH username, when the kind has one.</param>
/// <param name="AuthAlgorithm">The SNMP v3 authentication algorithm, for that kind alone.</param>
/// <param name="PrivacyAlgorithm">The SNMP v3 privacy algorithm, for that kind alone.</param>
/// <param name="DeviceCount">How many live devices it is assigned to.</param>
/// <param name="MaterialUpdatedAt">When the material was last replaced. UTC.</param>
/// <param name="CreatedAt">When the profile was created. UTC.</param>
/// <param name="UpdatedAt">When the row last changed. UTC.</param>
public sealed record CredentialProfileDetail(
    Guid Id,
    string Name,
    string? Description,
    CredentialKind Kind,
    string? Username,
    SnmpAuthAlgorithm? AuthAlgorithm,
    SnmpPrivacyAlgorithm? PrivacyAlgorithm,
    int DeviceCount,
    DateTimeOffset MaterialUpdatedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
