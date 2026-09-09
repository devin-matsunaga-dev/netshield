using Microsoft.EntityFrameworkCore;

using NetShield.Contracts.Inventory;

using NetShield.Inventory.Persistence;

namespace NetShield.Inventory.Topology.Handlers;

/// <summary>
/// The joins every VLAN read needs: how many clients are on a VLAN, and what a member port is
/// called.
/// </summary>
/// <remarks>
/// <para>
/// Written once because three handlers need them and the alternative is the same query in each,
/// differing in a filter. Both are the reason WP-2.2 aggregates on read rather than storing a
/// materialised VLAN row: a client count is a join against the open port bindings WP-1.8 writes,
/// which move every time somebody unplugs a laptop, so a stored copy would be wrong within
/// minutes of being written and would need invalidating from a subscriber in another feature.
/// </para>
/// <para>
/// <strong>Only live devices count.</strong> A soft-deleted device's VLAN rows and client bindings
/// survive as history — nothing prunes them, deliberately — and counting them would have a
/// removed switch still contributing to the tile an operator reads.
/// </para>
/// </remarks>
internal sealed class VlanReadJoins(InventoryDbContext context)
{
    /// <summary>
    /// How many distinct tracked clients are currently on each of these VLANs, estate-wide.
    /// </summary>
    /// <remarks>
    /// Distinct by client, because a MAC is learned by every bridge on the path to it and WP-1.8
    /// deliberately keeps one open port binding per client <em>and device</em>. Counting rows
    /// would count a laptop once for its access switch and again for every switch above it.
    /// </remarks>
    public async Task<IReadOnlyDictionary<int, int>> ClientCountsAsync(
        IReadOnlyCollection<int> vlanIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(vlanIds);

        if (vlanIds.Count == 0)
        {
            return new Dictionary<int, int>();
        }

        var counted = await context.ClientPortBindings.AsNoTracking()
            .Where(binding => binding.ObservedTo == null
                && binding.VlanId != null
                && vlanIds.Contains(binding.VlanId.Value)
                && context.Devices.Any(device =>
                    device.Id == binding.DeviceId && device.DeletedAt == null))
            .Select(binding => new { VlanId = binding.VlanId!.Value, binding.ClientId })
            .Distinct()
            .GroupBy(pair => pair.VlanId)
            .Select(group => new { VlanId = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        return counted.ToDictionary(row => row.VlanId, row => row.Count);
    }

    /// <summary>
    /// How many distinct clients each of these devices reports on each of these VLANs.
    /// </summary>
    /// <remarks>
    /// The same count sliced by device, which is what a per-device VLAN row shows and what makes
    /// the estate-wide number checkable: an operator who does not believe the tile can ask which
    /// switch is contributing what. The two do not have to add up — a client learned on an access
    /// switch and on the core above it counts once estate-wide and once for each of them here —
    /// and that is the truth about a forwarding database rather than an error.
    /// </remarks>
    public async Task<IReadOnlyDictionary<(Guid DeviceId, int VlanId), int>> ClientCountsByDeviceAsync(
        IReadOnlyCollection<Guid> deviceIds,
        IReadOnlyCollection<int> vlanIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(deviceIds);
        ArgumentNullException.ThrowIfNull(vlanIds);

        if (deviceIds.Count == 0 || vlanIds.Count == 0)
        {
            return new Dictionary<(Guid, int), int>();
        }

        var counted = await context.ClientPortBindings.AsNoTracking()
            .Where(binding => binding.ObservedTo == null
                && binding.VlanId != null
                && vlanIds.Contains(binding.VlanId.Value)
                && deviceIds.Contains(binding.DeviceId))
            .Select(binding => new
            {
                binding.DeviceId,
                VlanId = binding.VlanId!.Value,
                binding.ClientId
            })
            .Distinct()
            .GroupBy(pair => new { pair.DeviceId, pair.VlanId })
            .Select(group => new
            {
                group.Key.DeviceId,
                group.Key.VlanId,
                Count = group.Count()
            })
            .ToListAsync(cancellationToken);

        return counted.ToDictionary(row => (row.DeviceId, row.VlanId), row => row.Count);
    }

    /// <summary>
    /// What each of these devices calls each of its interfaces, for the ports a VLAN names.
    /// </summary>
    /// <remarks>
    /// Joined rather than stored on the VLAN row, because the interface inventory is WP-1.5's and
    /// a copy would go stale the first time an operator renamed a port. A port with no interface
    /// row is named <see langword="null"/> rather than dropped: a switch can carry a VLAN on a
    /// port whose fingerprint walk has not landed yet, and a membership that could not be named
    /// is still a membership.
    /// </remarks>
    public async Task<IReadOnlyDictionary<(Guid DeviceId, int IfIndex), string>> PortNamesAsync(
        IReadOnlyCollection<Guid> deviceIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(deviceIds);

        if (deviceIds.Count == 0)
        {
            return new Dictionary<(Guid, int), string>();
        }

        var rows = await context.DeviceInterfaces.AsNoTracking()
            .Where(row => deviceIds.Contains(row.DeviceId) && row.Name != null)
            .Select(row => new { row.DeviceId, row.IfIndex, row.Name })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(row => (row.DeviceId, row.IfIndex), row => row.Name!);
    }

    /// <summary>One VLAN's member ports on one device, named where the inventory knows them.</summary>
    public static IReadOnlyList<VlanPortMembership> Ports(
        DeviceVlan row,
        IReadOnlyDictionary<(Guid DeviceId, int IfIndex), string> names)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(names);

        HashSet<int> untagged = [.. row.UntaggedIfIndexes];

        return
        [
            .. row.IfIndexes.Order().Select(ifIndex => new VlanPortMembership(
                ifIndex,
                names.TryGetValue((row.DeviceId, ifIndex), out string? name) ? name : null,
                untagged.Contains(ifIndex)))
        ];
    }
}
