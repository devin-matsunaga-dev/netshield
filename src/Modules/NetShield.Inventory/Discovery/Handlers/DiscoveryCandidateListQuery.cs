using NetShield.Contracts.Inventory;

using NetShield.Platform.Paging;

namespace NetShield.Inventory.Discovery.Handlers;

/// <summary>One page of the discovery candidate review list.</summary>
/// <param name="Page">Which page, and how large.</param>
/// <param name="Status">
/// Restrict to candidates in one state. Absent means every state, which is what a caller
/// auditing what discovery has done wants; the review screen asks for
/// <see cref="DiscoveryCandidateStatus.New"/>.
/// </param>
internal sealed record DiscoveryCandidateListQuery(
    PageRequest Page,
    DiscoveryCandidateStatus? Status);
