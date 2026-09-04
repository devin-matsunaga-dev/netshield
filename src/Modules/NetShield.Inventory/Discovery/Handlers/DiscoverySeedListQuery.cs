using NetShield.Platform.Paging;

namespace NetShield.Inventory.Discovery.Handlers;

/// <summary>One page of the discovery seed list.</summary>
/// <param name="Page">Which page, and how large.</param>
/// <param name="Enabled">Restrict to seeds the schedule runs, or to those it does not.</param>
internal sealed record DiscoverySeedListQuery(PageRequest Page, bool? Enabled);
