using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory;
using NetShield.Contracts.Paging;

using NetShield.Inventory.Clients;
using NetShield.Inventory.Devices;
using NetShield.Inventory.Devices.Handlers;
using NetShield.Inventory.Discovery;
using NetShield.Inventory.Persistence;

using NetShield.Platform.Authorization;
using NetShield.Platform.Paging;
using NetShield.Platform.Results;

namespace NetShield.Inventory.Topology.Handlers;

/// <summary>
/// Serves one page of a device's ports and what is on the other end of each.
/// </summary>
/// <remarks>
/// <para>
/// <strong>No new collection.</strong> Every fact here was already being written by WP-1.5,
/// WP-1.8 and WP-2.1, keyed on <c>(device, ifIndex)</c> in all of them, and nothing has read them
/// together. What this adds is the join and the classification, both of which are decisions
/// rather than observations and are therefore made on read rather than stored.
/// </para>
/// <para>
/// <strong>The port set is a union, not the interface list.</strong> A switch can forward on a
/// port whose interface row no fingerprint walk has recorded yet, and a client binding carries an
/// <c>ifIndex</c> that is deliberately not a foreign key for exactly that reason. Paging over the
/// interface table alone would make such a port — and whatever is plugged into it — invisible
/// while the fingerprint catches up.
/// </para>
/// <para>
/// <strong>Two round trips over the clients, not one.</strong> The bindings are read first,
/// because their count is what decides whether a port is an uplink, and only then are the client
/// rows behind the ports that will actually list them fetched. An uplink on a busy estate carries
/// hundreds of addresses, and loading them to then throw them away would make the most expensive
/// port on the switch the one whose occupants are not shown.
/// </para>
/// </remarks>
internal sealed class GetDevicePortListHandler(
    InventoryDbContext context,
    IResourceGuard guard,
    IOptions<TopologyOptions> options)
{
    /// <summary>What one of this device's own observations said about the far end.</summary>
    private sealed record Observation(
        Guid AdjacencyId,
        NeighborSource Source,
        int? Capabilities,
        string? RemoteSystemDescription);

    /// <summary>A client row, before the binding that names its VLAN and its port is applied.</summary>
    private sealed record ClientFacts(
        Guid Id,
        string MacAddress,
        string? Hostname,
        string Oui,
        bool LocallyAdministered,
        string? IpAddress,
        DateTimeOffset LastSeenAt);

    public async Task<Result<CursorPage<DevicePortSummary>>> HandleAsync(
        Guid deviceId,
        PageRequest page,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);

        Result permitted = guard.Require(
            Permission.TopologyRead,
            GetDeviceListHandler.ResourceType,
            deviceId.ToString());

        if (!permitted.IsSuccess)
        {
            return Result<CursorPage<DevicePortSummary>>.Failure(permitted.Error);
        }

        // A device nothing has walked has no ports, and that is an empty page rather than a 404 —
        // the same split WP-1.7 drew between the interface list and the fingerprint, and WP-2.1
        // between the adjacency list and the topology scan.
        bool exists = await context.Devices.AsNoTracking()
            .AnyAsync(device => device.Id == deviceId && device.DeletedAt == null, cancellationToken);

        if (!exists)
        {
            return DeviceErrors.NotFound(deviceId);
        }

        IQueryable<int> indexes = PortIndexes(deviceId);

        long totalCount = await indexes.LongCountAsync(cancellationToken);

        if (page.Cursor is { } cursor)
        {
            Result<DevicePortCursor> position = DevicePortCursor.Decode(cursor);

            if (!position.IsSuccess)
            {
                return Result<CursorPage<DevicePortSummary>>.Failure(position.Error);
            }

            int from = position.Value.IfIndex;

            indexes = indexes.Where(index => index > from);
        }

        List<int> ordered = await indexes
            .OrderBy(index => index)
            .Take(page.FetchLimit)
            .ToListAsync(cancellationToken);

        CursorPage<int> window = ordered.ToCursorPage(page, DevicePortCursor.Compose, totalCount);
        IReadOnlyList<int> ports = window.Items;

        Dictionary<int, DeviceInterface> interfaces =
            await InterfacesAsync(deviceId, ports, cancellationToken);

        Dictionary<int, List<PortNeighbor>> neighbors =
            await NeighborsAsync(deviceId, ports, cancellationToken);

        Dictionary<int, List<ClientPortBinding>> bindings =
            await BindingsAsync(deviceId, ports, cancellationToken);

        int threshold = options.Value.UplinkAddressThreshold;

        Dictionary<int, PortOccupancyRule.PortClassification> classified = ports.ToDictionary(
            index => index,
            index => PortOccupancyRule.Classify(
                Facts(neighbors, index),
                LearnedAddressCount(bindings, index),
                Bindings(bindings, index).Count,
                threshold));

        Dictionary<Guid, ClientFacts> clients =
            await ClientsAsync(ports, bindings, classified, cancellationToken);

        return new CursorPage<DevicePortSummary>(
            [.. ports.Select(index => Summarise(
                index,
                interfaces.GetValueOrDefault(index),
                Neighbors(neighbors, index),
                Bindings(bindings, index),
                LearnedAddressCount(bindings, index),
                classified[index],
                clients))],
            window.NextCursor,
            window.TotalCount);
    }

    /// <summary>
    /// Every <c>ifIndex</c> this device has a port at, from all four sources, de-duplicated.
    /// </summary>
    /// <remarks>
    /// The device appears at the <c>A</c> end of some of its edges and the <c>B</c> end of others,
    /// because WP-2.1 orders an edge's endpoints canonically rather than by who observed it — so
    /// both ends have to be asked, or half a switch's uplinks would be missing.
    /// </remarks>
    private IQueryable<int> PortIndexes(Guid deviceId) =>
        context.DeviceInterfaces.AsNoTracking()
            .Where(row => row.DeviceId == deviceId)
            .Select(row => row.IfIndex)
            .Union(context.DeviceAdjacencies.AsNoTracking()
                .Where(edge => edge.WithdrawnAt == null && edge.ADeviceId == deviceId)
                .Select(edge => edge.AIfIndex))
            .Union(context.DeviceAdjacencies.AsNoTracking()
                .Where(edge => edge.WithdrawnAt == null
                    && edge.BDeviceId == deviceId
                    && edge.BIfIndex != null)
                .Select(edge => edge.BIfIndex!.Value))
            .Union(context.ClientPortBindings.AsNoTracking()
                .Where(binding => binding.DeviceId == deviceId && binding.ObservedTo == null)
                .Select(binding => binding.IfIndex));

    private async Task<Dictionary<int, DeviceInterface>> InterfacesAsync(
        Guid deviceId,
        IReadOnlyList<int> ports,
        CancellationToken cancellationToken)
    {
        if (ports.Count == 0)
        {
            return [];
        }

        List<DeviceInterface> rows = await context.DeviceInterfaces.AsNoTracking()
            .Where(row => row.DeviceId == deviceId && ports.Contains(row.IfIndex))
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(row => row.IfIndex);
    }

    /// <summary>
    /// The live edges on each of this page's ports, with the far end named and its capabilities
    /// decoded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read from <c>device_adjacencies</c> — what NetShield concluded — rather than from the
    /// observations behind it. An account WP-2.1's reconciler set aside carries no
    /// <c>AdjacencyId</c> and is deliberately absent here: it is evidence for a decision,
    /// returned in full by the adjacency route, and showing it as an occupant would put a second
    /// thing on a port that has one.
    /// </para>
    /// <para>
    /// <strong>Only this device's own observations describe the far end.</strong> An edge can be
    /// supported from both ends, and the far end's account of it describes <em>this</em> device —
    /// so reading a capability map off it would report the switch's own capabilities as the thing
    /// plugged into its port.
    /// </para>
    /// </remarks>
    private async Task<Dictionary<int, List<PortNeighbor>>> NeighborsAsync(
        Guid deviceId,
        IReadOnlyList<int> ports,
        CancellationToken cancellationToken)
    {
        if (ports.Count == 0)
        {
            return [];
        }

        List<DeviceAdjacency> edges = await context.DeviceAdjacencies.AsNoTracking()
            .Where(edge => edge.WithdrawnAt == null
                && ((edge.ADeviceId == deviceId && ports.Contains(edge.AIfIndex))
                    || (edge.BDeviceId == deviceId
                        && edge.BIfIndex != null
                        && ports.Contains(edge.BIfIndex!.Value))))
            .ToListAsync(cancellationToken);

        if (edges.Count == 0)
        {
            return [];
        }

        List<Guid> edgeIds = [.. edges.Select(edge => edge.Id)];

        List<Observation> observations = await context.DeviceNeighbors.AsNoTracking()
            .Where(row => row.WithdrawnAt == null
                && row.DeviceId == deviceId
                && row.AdjacencyId != null
                && edgeIds.Contains(row.AdjacencyId!.Value))
            .Select(row => new Observation(
                row.AdjacencyId!.Value,
                row.Source,
                row.Capabilities,
                row.RemoteSystemDescription))
            .ToListAsync(cancellationToken);

        ILookup<Guid, Observation> byEdge = observations.ToLookup(row => row.AdjacencyId);

        List<Guid> farDeviceIds = [.. edges
            .Select(edge => edge.ADeviceId == deviceId ? edge.BDeviceId : edge.ADeviceId)
            .OfType<Guid>()
            .Distinct()];

        Dictionary<Guid, string> hostnames = farDeviceIds.Count == 0
            ? []
            : await context.Devices.AsNoTracking()
                .Where(device => farDeviceIds.Contains(device.Id) && device.DeletedAt == null)
                .ToDictionaryAsync(device => device.Id, device => device.Hostname, cancellationToken);

        Dictionary<int, List<PortNeighbor>> grouped = [];

        foreach (DeviceAdjacency edge in edges)
        {
            bool nearIsA = edge.ADeviceId == deviceId;
            int nearIndex = nearIsA ? edge.AIfIndex : edge.BIfIndex!.Value;

            // Where this device is the `B` end the far end is the `A` end, which is a monitored
            // device by construction: WP-2.1 canonicalises an edge's endpoints only when both
            // ends resolved to devices.
            Guid? farDeviceId = nearIsA ? edge.BDeviceId : edge.ADeviceId;
            string? farHostname = farDeviceId is { } id ? hostnames.GetValueOrDefault(id) : null;
            IReadOnlyList<Observation> said = [.. byEdge[edge.Id]];

            grouped.TryAdd(nearIndex, []);
            grouped[nearIndex].Add(new PortNeighbor(
                edge.Id,
                farDeviceId,
                farDeviceId is not null,
                farHostname,
                nearIsA ? edge.BSystemName : farHostname,
                said.Select(row => row.RemoteSystemDescription).FirstOrDefault(text => text is not null),
                edge.BChassisId,
                edge.BChassisIdKind,
                nearIsA ? edge.BInterfaceName ?? edge.BPortId : edge.AInterfaceName,
                edge.Confidence,
                edge.ObservedFromA && edge.ObservedFromB,
                [.. Sources(edge)],
                SystemCapabilities.Merge(said.Select(row => (row.Source, row.Capabilities))),
                edge.FirstDiscoveredAt,
                edge.LastSeenAt));
        }

        return grouped;
    }

    /// <summary>The open client bindings on each of this page's ports.</summary>
    private async Task<Dictionary<int, List<ClientPortBinding>>> BindingsAsync(
        Guid deviceId,
        IReadOnlyList<int> ports,
        CancellationToken cancellationToken)
    {
        if (ports.Count == 0)
        {
            return [];
        }

        List<ClientPortBinding> rows = await context.ClientPortBindings.AsNoTracking()
            .Where(binding => binding.DeviceId == deviceId
                && binding.ObservedTo == null
                && ports.Contains(binding.IfIndex))
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(binding => binding.IfIndex)
            .ToDictionary(group => group.Key, group => group.ToList());
    }

    /// <summary>
    /// The client rows behind every port that will actually list them.
    /// </summary>
    /// <remarks>
    /// An uplink's clients are counted and never fetched. That is the difference between this
    /// route staying cheap on a core switch and it loading most of the estate's endpoints in
    /// order to discard them.
    /// </remarks>
    private async Task<Dictionary<Guid, ClientFacts>> ClientsAsync(
        IReadOnlyList<int> ports,
        IReadOnlyDictionary<int, List<ClientPortBinding>> bindings,
        IReadOnlyDictionary<int, PortOccupancyRule.PortClassification> classified,
        CancellationToken cancellationToken)
    {
        List<Guid> wanted = [.. ports
            .Where(index => classified[index].ClientsListed)
            .SelectMany(index => Bindings(bindings, index))
            .Select(binding => binding.ClientId)
            .Distinct()];

        if (wanted.Count == 0)
        {
            return [];
        }

        Dictionary<Guid, string> addresses = await context.ClientIpBindings.AsNoTracking()
            .Where(binding => binding.ObservedTo == null && wanted.Contains(binding.ClientId))
            .OrderByDescending(binding => binding.ObservedFrom)
            .GroupBy(binding => binding.ClientId)
            .Select(group => group.First())
            .ToDictionaryAsync(
                binding => binding.ClientId,
                binding => binding.IpAddress.ToString(),
                cancellationToken);

        List<Client> rows = await context.Clients.AsNoTracking()
            .Where(client => wanted.Contains(client.Id))
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(
            client => client.Id,
            client => new ClientFacts(
                client.Id,
                client.MacAddress,
                client.Hostname,
                client.Oui,
                client.LocallyAdministered,
                addresses.GetValueOrDefault(client.Id),
                client.LastSeenAt));
    }

    private static DevicePortSummary Summarise(
        int ifIndex,
        DeviceInterface? port,
        IReadOnlyList<PortNeighbor> neighbors,
        IReadOnlyList<ClientPortBinding> bindings,
        int? learnedAddressCount,
        PortOccupancyRule.PortClassification classification,
        IReadOnlyDictionary<Guid, ClientFacts> clients) =>
        new(
            ifIndex,
            port?.Name,
            port?.Description,
            port?.Alias,
            port is not null,
            FingerprintMapping.ToAdminStatus(port?.AdminStatus),
            FingerprintMapping.ToOperStatus(port?.OperStatus),
            classification.Role,
            classification.Reason,
            learnedAddressCount,
            bindings.Count,
            classification.ClientsListed,
            neighbors,
            classification.ClientsListed ? Occupants(bindings, clients) : []);

    /// <summary>
    /// The clients on one access port, ordered by address so two reads agree.
    /// </summary>
    /// <remarks>
    /// The VLAN and the interval come from the binding rather than from the client, because they
    /// are facts about this port's observation of it: the same laptop on a different switch was
    /// learned at a different time and, on a trunk, possibly in a different VLAN.
    /// </remarks>
    private static IReadOnlyList<PortClient> Occupants(
        IReadOnlyList<ClientPortBinding> bindings,
        IReadOnlyDictionary<Guid, ClientFacts> clients) =>
        [.. bindings
            .Select(binding => clients.TryGetValue(binding.ClientId, out ClientFacts? client)
                ? new PortClient(
                    client.Id,
                    client.MacAddress,
                    client.Hostname,
                    client.Oui,
                    client.LocallyAdministered,
                    client.IpAddress,
                    binding.VlanId,
                    binding.ObservedFrom,
                    client.LastSeenAt)
                : null)
            .OfType<PortClient>()
            .OrderBy(client => client.MacAddress, StringComparer.Ordinal)];

    /// <summary>
    /// Both sides' protocol lists as one, because a caller asking what is on a port wants every
    /// protocol that saw it rather than which end reported which.
    /// </summary>
    private static IEnumerable<NeighborSource> Sources(DeviceAdjacency edge) =>
        edge.SourcesA
            .Concat(edge.SourcesB)
            .Select(name => Enum.TryParse(name, out NeighborSource source) ? (NeighborSource?)source : null)
            .OfType<NeighborSource>()
            .Distinct()
            .Order();

    private static IReadOnlyList<ClientPortBinding> Bindings(
        IReadOnlyDictionary<int, List<ClientPortBinding>> bindings,
        int ifIndex) =>
        bindings.TryGetValue(ifIndex, out List<ClientPortBinding>? found) ? found : [];

    private static IReadOnlyList<PortNeighbor> Neighbors(
        IReadOnlyDictionary<int, List<PortNeighbor>> neighbors,
        int ifIndex) =>
        neighbors.TryGetValue(ifIndex, out List<PortNeighbor>? found) ? found : [];

    private static IReadOnlyList<PortOccupancyRule.NeighborFacts> Facts(
        IReadOnlyDictionary<int, List<PortNeighbor>> neighbors,
        int ifIndex) =>
        [.. Neighbors(neighbors, ifIndex).Select(neighbor =>
            new PortOccupancyRule.NeighborFacts(neighbor.Managed, neighbor.Capabilities))];

    /// <summary>
    /// The switch's own count of what the port had learned — the largest any binding on it
    /// recorded.
    /// </summary>
    /// <remarks>
    /// The bindings on one port are written by one walk and carry the same count, so the maximum
    /// is that count; it is a maximum rather than a first so that a binding left over from an
    /// earlier, smaller reading cannot under-report the port. Null where the device answered only
    /// the VLAN-unaware forwarding database, which records no count at all — and null rather than
    /// zero, because "the agent did not say" and "the port has learned nothing" are different
    /// answers and only one of them is evidence.
    /// </remarks>
    private static int? LearnedAddressCount(
        IReadOnlyDictionary<int, List<ClientPortBinding>> bindings,
        int ifIndex)
    {
        List<int> counts = [.. Bindings(bindings, ifIndex)
            .Select(binding => binding.MacCountOnPort)
            .OfType<int>()];

        return counts.Count == 0 ? null : counts.Max();
    }
}
