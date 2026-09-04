using NetShield.Contracts.Inventory;

namespace NetShield.Inventory.Credentials;

/// <summary>
/// Turns the entity into the shapes that leave the module — which is every describable attribute
/// and none of the sealed ones.
/// </summary>
/// <remarks>
/// The one place the boundary in ARCHITECTURE.md §4 is crossed for a credential profile, and so
/// the one place that would have to be edited for a secret to escape. It has no access to
/// plaintext at all: opening the blob needs <see cref="CredentialMaterialProtector"/>, and
/// nothing here takes one.
/// </remarks>
internal static class CredentialProfileMapping
{
    internal static CredentialProfileDetail ToDetail(this CredentialProfile profile, int deviceCount) =>
        new(
            profile.Id,
            profile.Name,
            profile.Description,
            profile.Kind,
            profile.Username,
            profile.AuthAlgorithm,
            profile.PrivacyAlgorithm,
            deviceCount,
            profile.MaterialUpdatedAt,
            profile.CreatedAt,
            profile.UpdatedAt);

    internal static CredentialProfileSummary ToSummary(this CredentialProfile profile, int deviceCount) =>
        new(
            profile.Id,
            profile.Name,
            profile.Kind,
            profile.Username,
            deviceCount,
            profile.MaterialUpdatedAt,
            profile.UpdatedAt);

    /// <summary>
    /// What an audit row records about a profile.
    /// </summary>
    /// <remarks>
    /// Every key is chosen so that <c>SecretRedactor</c> leaves it alone and so that nothing
    /// needing redaction is here to begin with — the material is not in this dictionary, in any
    /// form, not even a length or a hash. <c>materialUpdatedAt</c> rather than
    /// <c>secretUpdatedAt</c> for the first reason: the honest-looking name is one the redactor
    /// blanks by name, and the row would have recorded <c>[REDACTED]</c> for a timestamp.
    /// </remarks>
    internal static IReadOnlyDictionary<string, object?> ToAuditSnapshot(this CredentialProfile profile) =>
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = profile.Name,
            ["description"] = profile.Description,
            ["kind"] = profile.Kind.ToString(),
            ["username"] = profile.Username,
            ["authAlgorithm"] = profile.AuthAlgorithm?.ToString(),
            ["privacyAlgorithm"] = profile.PrivacyAlgorithm?.ToString(),
            ["materialUpdatedAt"] = profile.MaterialUpdatedAt.ToString("O")
        };
}
