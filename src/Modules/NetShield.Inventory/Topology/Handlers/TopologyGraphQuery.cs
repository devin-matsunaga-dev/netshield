using NetShield.Platform.Paging;
using NetShield.Platform.Results;

namespace NetShield.Inventory.Topology.Handlers;

/// <summary>
/// A validated request for one page of the topology graph.
/// </summary>
/// <remarks>
/// <para>
/// Validated at construction, in the shape <see cref="PageRequest.Create"/> is, rather than by a
/// FluentValidation validator: <c>CONVENTIONS.md</c> §4 puts validation at the endpoint boundary
/// and the boundary for a <c>GET</c> is its query string, which the device list already reads the
/// same way. What reaches the handler is a request that cannot be malformed.
/// </para>
/// <para>
/// <strong><see cref="Depth"/> only means something with a <see cref="RootDeviceId"/>.</strong>
/// The two are the bound on the traversal and they are refused apart, rather than one being
/// quietly ignored.
/// </para>
/// </remarks>
/// <param name="Page">How many nodes to return, and where to resume from.</param>
/// <param name="Site">Only devices at this site. Matched exactly, ignoring case, as the device list matches it.</param>
/// <param name="VlanId">Only devices carrying this VLAN, and the edges between them.</param>
/// <param name="RootDeviceId">The device to centre on, where the caller named one.</param>
/// <param name="Depth">How many hops from that root to walk. Meaningless, and refused, without a root.</param>
internal sealed record TopologyGraphQuery(
    PageRequest Page,
    string? Site,
    int? VlanId,
    Guid? RootDeviceId,
    int? Depth)
{
    /// <summary>
    /// Reads a caller's graph arguments, or says why they cannot be read.
    /// </summary>
    internal static Result<TopologyGraphQuery> Create(
        PageRequest page,
        string? site,
        int? vlanId,
        Guid? rootDeviceId,
        int? depth)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (vlanId is { } vlan && (vlan < TopologyLimits.MinVlanId || vlan > TopologyLimits.MaxVlanId))
        {
            return TopologyErrors.VlanIdOutOfRange(vlan);
        }

        if (depth is { } requested)
        {
            if (rootDeviceId is null)
            {
                return TopologyErrors.GraphDepthWithoutRoot();
            }

            if (requested < 0 || requested > TopologyLimits.MaxGraphDepth)
            {
                return TopologyErrors.GraphDepthOutOfRange(requested);
            }
        }

        return new TopologyGraphQuery(
            page,
            string.IsNullOrWhiteSpace(site) ? null : site.Trim(),
            vlanId,
            rootDeviceId,
            rootDeviceId is null ? null : depth ?? TopologyLimits.DefaultGraphDepth);
    }
}
