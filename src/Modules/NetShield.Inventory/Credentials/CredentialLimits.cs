namespace NetShield.Inventory.Credentials;

/// <summary>
/// The bounds the validators enforce and the column widths the mapping declares, in one place so
/// the two cannot drift into a request the API accepts and the database refuses.
/// </summary>
internal static class CredentialLimits
{
    /// <summary>The longest profile name.</summary>
    internal const int NameLength = 128;

    /// <summary>The longest description.</summary>
    internal const int DescriptionLength = 1000;

    /// <summary>The longest SNMP v3 security name or SSH username.</summary>
    internal const int UsernameLength = 128;

    /// <summary>The longest key-encryption key id.</summary>
    internal const int KeyIdLength = 64;

    /// <summary>
    /// The longest community string or pass phrase. Generous: it bounds a request rather than
    /// expressing an opinion about how long a secret ought to be.
    /// </summary>
    internal const int SecretLength = 1024;

    /// <summary>
    /// The longest private key. A PEM RSA-4096 key with a certificate chain fits inside this
    /// several times over.
    /// </summary>
    internal const int PrivateKeyLength = 32 * 1024;

    /// <summary>The most profiles one device may be assigned.</summary>
    internal const int MaximumAssignmentsPerDevice = 16;

    /// <summary>Case-folds a name into the form uniqueness is decided on.</summary>
    internal static string NormalizeName(string name) => name.Trim().ToLowerInvariant();
}
