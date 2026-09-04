using NetShield.Contracts.Inventory;

using NetShield.Platform.Results;

namespace NetShield.Inventory.Credentials;

/// <summary>
/// Every refusal the credential handlers can return, in one place, so the codes a client branches
/// on are visible together (CONVENTIONS.md §4).
/// </summary>
/// <remarks>
/// No message here names a secret, a key id, or any part of the material — a refusal is read by
/// whoever sent the request, and SPEC.md §5 admits no credential in an API response.
/// </remarks>
internal static class CredentialErrors
{
    internal const string NotFoundCode = "credential-profile.not-found";
    internal const string DuplicateNameCode = "credential-profile.duplicate-name";
    internal const string UnknownSortCode = "credential-profile.unknown-sort";
    internal const string MaterialIncompleteCode = "credential-profile.material-incomplete";
    internal const string AttributesInvalidCode = "credential-profile.attributes-invalid";
    internal const string TooManyAssignmentsCode = "credential-profile.too-many-assignments";

    internal static Error NotFound(Guid id) =>
        Error.NotFound(NotFoundCode, $"No credential profile with id {id}.");

    internal static Error DeviceNotFound(Guid id) =>
        Error.NotFound("device.not-found", $"No device with id {id}.");

    internal static Error DuplicateName(string name) =>
        Error.Conflict(DuplicateNameCode, $"Another credential profile is already called '{name}'.");

    internal static Error UnknownSort(string field, IEnumerable<string> permitted) =>
        Error.Validation(
            UnknownSortCode,
            $"Cannot sort by '{field}'.",
            new Dictionary<string, string[]>
            {
                ["sort"] = [$"Must be one of: {string.Join(", ", permitted)}."]
            });

    /// <summary>
    /// The request is well-formed but does not carry what this kind needs, so 422 rather than
    /// 400: which members are required is a fact about the profile's kind, not about the shape
    /// (CONVENTIONS.md §4).
    /// </summary>
    internal static Error MaterialIncomplete(CredentialKind kind, string detail) =>
        Error.Unprocessable(MaterialIncompleteCode, $"A {Describe(kind)} credential {detail}");

    internal static Error AttributesInvalid(CredentialKind kind, string detail) =>
        Error.Unprocessable(AttributesInvalidCode, $"A {Describe(kind)} credential {detail}");

    internal static Error TooManyAssignments(int limit) =>
        Error.Unprocessable(
            TooManyAssignmentsCode,
            $"A device may be assigned at most {limit} credential profiles.");

    /// <summary>The kind as it reads in a sentence.</summary>
    private static string Describe(CredentialKind kind) => kind switch
    {
        CredentialKind.SnmpV2c => "SNMP v2c",
        CredentialKind.SnmpV3 => "SNMP v3",
        CredentialKind.SshPassword => "SSH password",
        _ => "SSH key"
    };
}
