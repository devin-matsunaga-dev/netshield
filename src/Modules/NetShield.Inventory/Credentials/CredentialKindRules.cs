using NetShield.Contracts.Inventory;

using NetShield.Platform.Results;

namespace NetShield.Inventory.Credentials;

/// <summary>
/// What each <see cref="CredentialKind"/> requires of a profile's attributes and of its material.
/// </summary>
/// <remarks>
/// <para>
/// These are semantic rules, not shape rules, so they live here and answer 422 rather than living
/// in a FluentValidation validator and answering 400 (CONVENTIONS.md §4). "This request has a
/// <c>community</c> member" is a question about the request; "an SNMP v3 credential needs an
/// authentication pass phrase and not a community" is a question about what was asked for.
/// </para>
/// <para>
/// Both directions are checked: a required member missing is a refusal, and a member that belongs
/// to another kind is also a refusal. Ignoring the second would silently store an SSH password
/// inside an SNMP profile, where nothing would ever read it and nothing would ever say so.
/// </para>
/// </remarks>
internal static class CredentialKindRules
{
    /// <summary>
    /// Whether the describable attributes suit the kind. The algorithms belong to SNMP v3 alone,
    /// and every kind but SNMP v2c needs a username.
    /// </summary>
    internal static Result CheckAttributes(
        CredentialKind kind,
        string? username,
        SnmpAuthAlgorithm? authAlgorithm,
        SnmpPrivacyAlgorithm? privacyAlgorithm)
    {
        bool hasUsername = !string.IsNullOrWhiteSpace(username);

        if (kind is CredentialKind.SnmpV2c)
        {
            if (hasUsername)
            {
                return CredentialErrors.AttributesInvalid(kind, "has no username; it is a community string alone.");
            }
        }
        else if (!hasUsername)
        {
            return CredentialErrors.AttributesInvalid(
                kind,
                kind is CredentialKind.SnmpV3
                    ? "needs a username, which is its security name."
                    : "needs a username to connect as.");
        }

        if (kind is CredentialKind.SnmpV3)
        {
            if (authAlgorithm is null || privacyAlgorithm is null)
            {
                return CredentialErrors.AttributesInvalid(
                    kind,
                    "needs both an authentication algorithm and a privacy algorithm. "
                    + "Use privacy 'None' for authNoPriv.");
            }
        }
        else if (authAlgorithm is not null || privacyAlgorithm is not null)
        {
            return CredentialErrors.AttributesInvalid(kind, "carries no SNMP algorithms.");
        }

        return Result.Success;
    }

    /// <summary>
    /// Whether the material carries exactly what the kind needs, given the privacy algorithm the
    /// profile was created with.
    /// </summary>
    internal static Result CheckMaterial(
        CredentialKind kind,
        SnmpPrivacyAlgorithm? privacyAlgorithm,
        CredentialMaterialPayload material)
    {
        ArgumentNullException.ThrowIfNull(material);

        bool privacyRequired = kind is CredentialKind.SnmpV3 && privacyAlgorithm is not SnmpPrivacyAlgorithm.None;

        (string Name, bool Required, bool Present)[] members =
        [
            ("community", kind is CredentialKind.SnmpV2c, material.Community is not null),
            ("authPassword", kind is CredentialKind.SnmpV3, material.AuthPassword is not null),
            ("privacyPassword", privacyRequired, material.PrivacyPassword is not null),
            ("password", kind is CredentialKind.SshPassword, material.Password is not null),
            ("privateKey", kind is CredentialKind.SshKey, material.PrivateKey is not null),

            // The only optional member anywhere: a private key may or may not be protected, and
            // both are ordinary.
            ("privateKeyPassword", false, material.PrivateKeyPassword is not null)
        ];

        IReadOnlyList<string> missing =
            [.. members.Where(member => member.Required && !member.Present).Select(member => member.Name)];

        if (missing.Count > 0)
        {
            return CredentialErrors.MaterialIncomplete(
                kind,
                $"needs {Join(missing)}, which {(missing.Count == 1 ? "is" : "are")} missing.");
        }

        // An unexpected member is refused rather than dropped. Dropping it would accept a request
        // that says one thing and store another, and the caller would have no way to notice.
        IReadOnlyList<string> unexpected =
        [
            .. members
                .Where(member => member.Present && !member.Required)
                .Where(member => member.Name is not "privateKeyPassword" || kind is not CredentialKind.SshKey)
                .Select(member => member.Name)
        ];

        return unexpected.Count > 0
            ? CredentialErrors.MaterialIncomplete(kind, $"carries no {Join(unexpected)}.")
            : Result.Success;
    }

    private static string Join(IReadOnlyList<string> names) =>
        names.Count == 1 ? names[0] : $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}";
}
