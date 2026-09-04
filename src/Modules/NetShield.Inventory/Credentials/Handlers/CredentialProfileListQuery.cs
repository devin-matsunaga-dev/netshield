using NetShield.Contracts.Inventory;

using NetShield.Platform.Paging;

namespace NetShield.Inventory.Credentials.Handlers;

/// <summary>One page of the credential profile list, as the endpoint parsed it.</summary>
/// <param name="Page">The validated cursor and limit.</param>
/// <param name="Sort">Which field to order by.</param>
/// <param name="Descending">Whether to reverse that order.</param>
/// <param name="Kind">Show only profiles of this kind.</param>
/// <param name="Search">A name prefix, case-insensitively.</param>
internal sealed record CredentialProfileListQuery(
    PageRequest Page,
    CredentialProfileSortField Sort,
    bool Descending,
    CredentialKind? Kind,
    string? Search);
