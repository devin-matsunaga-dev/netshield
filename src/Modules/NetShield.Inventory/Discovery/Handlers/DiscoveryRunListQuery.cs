using NetShield.Contracts.Inventory;

using NetShield.Platform.Paging;

namespace NetShield.Inventory.Discovery.Handlers;

/// <summary>One page of the discovery run history.</summary>
/// <param name="Page">Which page, and how large.</param>
/// <param name="SeedId">Restrict to the runs of one seed.</param>
/// <param name="Status">Restrict to runs in one state.</param>
internal sealed record DiscoveryRunListQuery(
    PageRequest Page,
    Guid? SeedId,
    DiscoveryRunStatus? Status);
