namespace NetShield.Contracts.Inventory;

/// <summary>
/// One row of the credential profile list.
/// </summary>
/// <remarks>
/// It carries no secret member and there is deliberately no way to add one. A profile's material
/// is write-only over the API (SPEC.md §5): it goes in, it is encrypted, and the only thing that
/// ever reads it back is the decrypt path inside the Inventory module. An architecture test walks
/// every response schema in the OpenAPI document and fails the build if a member appears here
/// that <c>SecretRedactor</c> would call a secret.
/// </remarks>
/// <param name="Id">The profile.</param>
/// <param name="Name">What an operator calls it.</param>
/// <param name="Kind">Which protocol it authenticates.</param>
/// <param name="Username">The SNMP v3 security name or the SSH username, when the kind has one.</param>
/// <param name="DeviceCount">How many live devices it is assigned to.</param>
/// <param name="MaterialUpdatedAt">When the material was last replaced. UTC.</param>
/// <param name="UpdatedAt">When the row last changed. UTC.</param>
public sealed record CredentialProfileSummary(
    Guid Id,
    string Name,
    CredentialKind Kind,
    string? Username,
    int DeviceCount,
    DateTimeOffset MaterialUpdatedAt,
    DateTimeOffset UpdatedAt);
