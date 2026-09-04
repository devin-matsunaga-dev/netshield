namespace NetShield.Inventory.Credentials.Handlers;

/// <summary>The fields the credential profile list can be ordered by.</summary>
/// <remarks>
/// Two, and both are stable and indexed. A sort by anything else is refused rather than ignored
/// — WP-1.1 settled that a caller who misspells a field and is served a differently ordered page
/// has no way to notice.
/// </remarks>
internal enum CredentialProfileSortField
{
    /// <summary>Newest last. The default, and the order the keyset walks by id.</summary>
    CreatedAt,

    /// <summary>Alphabetical by the name an operator gave it.</summary>
    Name
}
