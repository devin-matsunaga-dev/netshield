using Microsoft.EntityFrameworkCore;

using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory;

using NetShield.Inventory.Persistence;

using NetShield.Platform.Authorization;
using NetShield.Platform.Results;

namespace NetShield.Inventory.Topology.Handlers;

/// <summary>
/// Serves one VLAN, with every device carrying it and the ports each carries it on.
/// </summary>
/// <remarks>
/// <see cref="GetVlanListHandler"/> with the memberships behind it. The list answers "what VLANs
/// does this estate have and how big is each"; this answers "and where exactly", which is the
/// question an operator asks next and the one a per-device page could only be assembled into by
/// walking every device.
///
/// The device list is not paginated, and deliberately: a VLAN spans switches rather than
/// endpoints, so at SPEC.md §1's 500 devices the worst case is 500 rows and the realistic case is
/// a handful. If an estate ever makes that wrong, it is the same fix a paginated sub-resource
/// always is, and pretending it is needed now would cost a cursor nobody would use.
/// </remarks>
internal sealed class GetVlanHandler(
    InventoryDbContext context,
    VlanReadJoins joins,
    IResourceGuard guard)
{
    public async Task<Result<VlanDetail>> HandleAsync(
        int vlanId,
        CancellationToken cancellationToken)
    {
        Result permitted = guard.Require(
            Permission.TopologyRead,
            GetVlanListHandler.ResourceType,
            vlanId.ToString(System.Globalization.CultureInfo.InvariantCulture));

        if (!permitted.IsSuccess)
        {
            return Result<VlanDetail>.Failure(permitted.Error);
        }

        if (vlanId < TopologyLimits.MinVlanId || vlanId > TopologyLimits.MaxVlanId)
        {
            return TopologyErrors.VlanIdOutOfRange(vlanId);
        }

        List<DeviceVlan> rows = await GetVlanListHandler.LiveRows(context)
            .Where(row => row.VlanId == vlanId)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return TopologyErrors.VlanNotFound(vlanId);
        }

        List<Guid> deviceIds = [.. rows.Select(row => row.DeviceId).Distinct()];

        IReadOnlyDictionary<Guid, string> hostnames = await HostnamesAsync(
            deviceIds,
            cancellationToken);

        IReadOnlyDictionary<(Guid, int), string> portNames = await joins.PortNamesAsync(
            deviceIds,
            cancellationToken);

        IReadOnlyDictionary<(Guid, int), int> perDevice = await joins.ClientCountsByDeviceAsync(
            deviceIds,
            [vlanId],
            cancellationToken);

        IReadOnlyDictionary<int, int> estate = await joins.ClientCountsAsync(
            [vlanId],
            cancellationToken);

        VlanAggregationRule.Aggregated aggregated = VlanAggregationRule.Aggregate(
        [
            .. rows.Select(row => new VlanAggregationRule.Membership(
                row.DeviceId,
                row.Name,
                row.IfIndexes.Length,
                row.FirstDiscoveredAt,
                row.LastSeenAt))
        ]);

        List<VlanDeviceMembership> devices =
        [
            .. rows
                .Where(row => hostnames.ContainsKey(row.DeviceId))
                .Select(row => new VlanDeviceMembership(
                    row.DeviceId,
                    hostnames[row.DeviceId],
                    row.Name,
                    VlanReadJoins.Ports(row, portNames),
                    row.IfIndexes.Length,
                    perDevice.TryGetValue((row.DeviceId, vlanId), out int count) ? count : 0,
                    row.FirstDiscoveredAt,
                    row.LastSeenAt))
                .OrderBy(membership => membership.Hostname, StringComparer.Ordinal)
                .ThenBy(membership => membership.DeviceId)
        ];

        return new VlanDetail(
            vlanId,
            aggregated.Name,
            aggregated.NameDisputed,
            aggregated.Names,
            aggregated.DeviceCount,
            estate.TryGetValue(vlanId, out int clients) ? clients : 0,
            aggregated.PortCount,
            aggregated.FirstDiscoveredAt,
            aggregated.LastSeenAt,
            devices);
    }

    private async Task<IReadOnlyDictionary<Guid, string>> HostnamesAsync(
        IReadOnlyCollection<Guid> deviceIds,
        CancellationToken cancellationToken)
    {
        var rows = await context.Devices.AsNoTracking()
            .Where(device => deviceIds.Contains(device.Id) && device.DeletedAt == null)
            .Select(device => new { device.Id, device.Hostname })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(row => row.Id, row => row.Hostname);
    }
}
