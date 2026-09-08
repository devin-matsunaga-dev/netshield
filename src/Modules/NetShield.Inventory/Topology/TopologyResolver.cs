using System.Net;

using Microsoft.EntityFrameworkCore;

using NetShield.Contracts.Inventory;

using NetShield.Inventory.Clients;
using NetShield.Inventory.Persistence;

namespace NetShield.Inventory.Topology;

/// <summary>
/// Turns "something called <c>core-sw-1</c> on port <c>Ethernet1</c>" into a device and an
/// interface NetShield already has, where it can.
/// </summary>
/// <remarks>
/// <para>
/// This is what makes an edge an edge rather than two unrelated sightings. Until the far end is
/// resolved, an LLDP account naming a chassis address and a CDP account naming a host name are
/// two different strings about the same switch; once both resolve to the same device row they
/// share an adjacency key and merge into one edge carrying both sources.
/// </para>
/// <para>
/// <strong>Three routes, in descending order of how much they prove.</strong> A management
/// address is an address NetShield reaches the device on, so matching it against
/// <c>devices.primary_ip_address</c> is as good as this gets. A chassis identifier of kind
/// <see cref="NeighborIdKind.MacAddress"/> is matched against the interface inventory WP-1.5
/// already records, which is a hardware fact. A system name is matched against
/// <c>devices.hostname</c> <em>last</em>, because WP-1.1 settled that a hostname is not an
/// identity — but it is the only thing CDP usually gives, so refusing to use it at all would
/// leave every CDP-only edge unresolved.
/// </para>
/// <para>
/// <strong>Resolution is redone on every walk and never cached on the row.</strong> A neighbour
/// that was a stranger last week may have been promoted from a discovery candidate since, and the
/// edge should become a device-to-device one without anything having to notice.
/// </para>
/// </remarks>
internal sealed class TopologyResolver(InventoryDbContext context)
{
    /// <summary>Resolves every observation's far end, returning them with the answers filled in.</summary>
    public async Task<IReadOnlyList<NeighborObservation>> ResolveAsync(
        IReadOnlyList<NeighborObservation> observations,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(observations);

        if (observations.Count == 0)
        {
            return observations;
        }

        DeviceIndex devices = await LoadDevicesAsync(cancellationToken);
        IReadOnlyDictionary<string, Guid> byMac = await LoadInterfaceMacsAsync(
            observations,
            cancellationToken);

        List<NeighborObservation> resolved = new(observations.Count);

        foreach (NeighborObservation observation in observations)
        {
            Guid? deviceId = Resolve(observation, devices, byMac);

            resolved.Add(observation with { RemoteDeviceId = deviceId });
        }

        // The far end's own interface, in one query over only the devices actually resolved.
        // It is what lets both ends of a link compute the same canonical pair and converge on
        // one adjacency row; without it each end writes its own, which is honest and is twice
        // as many edges.
        InterfaceIndex interfaces = await LoadInterfacesAsync(resolved, cancellationToken);

        for (int index = 0; index < resolved.Count; index++)
        {
            int? ifIndex = ResolvePort(resolved[index], interfaces.ByName);

            resolved[index] = resolved[index] with
            {
                RemoteIfIndex = ifIndex,
                RemoteInterfaceName = ifIndex is { } port
                    && resolved[index].RemoteDeviceId is { } device
                    && interfaces.Names.TryGetValue((device, port), out string? name)
                        ? name
                        : null
            };
        }

        return resolved;
    }

    private static Guid? Resolve(
        NeighborObservation observation,
        DeviceIndex devices,
        IReadOnlyDictionary<string, Guid> byMac)
    {
        if (observation.RemoteManagementAddress is { } address
            && devices.ByAddress.TryGetValue(address, out Guid byAddress))
        {
            return byAddress;
        }

        // A network-address chassis id is an address the far end chose to identify itself by,
        // which is exactly as good as a management address for this purpose.
        if (observation.RemoteChassisIdKind == NeighborIdKind.NetworkAddress
            && IPAddress.TryParse(observation.RemoteChassisId, out IPAddress? advertised)
            && devices.ByAddress.TryGetValue(advertised, out Guid byAdvertised))
        {
            return byAdvertised;
        }

        if (observation.RemoteChassisIdKind == NeighborIdKind.MacAddress
            && MacAddress.Normalize(observation.RemoteChassisId) is { } mac
            && byMac.TryGetValue(mac, out Guid byHardware))
        {
            return byHardware;
        }

        foreach (string? name in new[] { observation.RemoteSystemName, observation.RemoteChassisId })
        {
            if (name is { Length: > 0 }
                && devices.ByHostname.TryGetValue(name.Trim(), out Guid byHostname))
            {
                return byHostname;
            }
        }

        return null;
    }

    /// <summary>
    /// Which interface on the far device the advertised port names.
    /// </summary>
    /// <remarks>
    /// Matched against both <c>ifName</c> and <c>ifDescr</c>, because a neighbour protocol
    /// advertises the short form — <c>Et1</c>, <c>Gi0/1</c> — and the inventory holds both. A
    /// port the far device has never reported resolves to nothing, and the edge is then written
    /// with the observing device as its <c>A</c> end rather than being canonicalised: NetShield
    /// has not established that the two accounts are of one link.
    /// </remarks>
    private static int? ResolvePort(
        NeighborObservation observation,
        IReadOnlyDictionary<(Guid, string), int> byName)
    {
        if (observation.RemoteDeviceId is not { } deviceId
            || observation.RemotePortId is not { Length: > 0 } port)
        {
            return null;
        }

        return byName.TryGetValue((deviceId, port.Trim().ToLowerInvariant()), out int ifIndex)
            ? ifIndex
            : null;
    }

    /// <summary>
    /// Every live device's address and hostname, in one query.
    /// </summary>
    /// <remarks>
    /// SPEC.md §1 targets 500 devices, so this is five hundred short rows read once per walk
    /// application. Two indexed lookups would be cheaper per row and would need the address
    /// comparison to translate through <c>inet</c>, which EF cannot express without raw SQL — the
    /// same reason <c>ix_devices_primary_ip_address_live</c> is hand-written. At this scale the
    /// simple thing is also the fast thing; if the estate ever outgrows it, this is the query to
    /// narrow.
    /// </remarks>
    private async Task<DeviceIndex> LoadDevicesAsync(CancellationToken cancellationToken)
    {
        var rows = await context.Devices.AsNoTracking()
            .Where(device => device.DeletedAt == null)
            .Select(device => new
            {
                device.Id,
                device.Hostname,
                device.PrimaryIpAddress
            })
            .ToListAsync(cancellationToken);

        Dictionary<IPAddress, Guid> byAddress = [];
        Dictionary<string, Guid> byHostname = new(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            byAddress.TryAdd(row.PrimaryIpAddress, row.Id);

            // A hostname is deliberately not unique (WP-1.1), so two devices can share one and
            // the lower id wins rather than the one a query happened to return first. It is the
            // weakest of the three routes for exactly this reason.
            if (!byHostname.TryGetValue(row.Hostname, out Guid existing) || row.Id < existing)
            {
                byHostname[row.Hostname] = row.Id;
            }
        }

        return new DeviceIndex(byAddress, byHostname);
    }

    /// <summary>The devices owning the hardware addresses these observations advertised.</summary>
    private async Task<IReadOnlyDictionary<string, Guid>> LoadInterfaceMacsAsync(
        IReadOnlyList<NeighborObservation> observations,
        CancellationToken cancellationToken)
    {
        List<string> macs = [.. observations
            .Where(observation => observation.RemoteChassisIdKind == NeighborIdKind.MacAddress)
            .Select(observation => MacAddress.Normalize(observation.RemoteChassisId))
            .OfType<string>()
            .Distinct()];

        if (macs.Count == 0)
        {
            return new Dictionary<string, Guid>(StringComparer.Ordinal);
        }

        var rows = await context.DeviceInterfaces.AsNoTracking()
            .Where(port => port.PhysicalAddress != null && macs.Contains(port.PhysicalAddress))
            .Select(port => new { port.DeviceId, port.PhysicalAddress })
            .ToListAsync(cancellationToken);

        Dictionary<string, Guid> byMac = new(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            if (MacAddress.Normalize(row.PhysicalAddress) is { } normalized)
            {
                byMac.TryAdd(normalized, row.DeviceId);
            }
        }

        return byMac;
    }

    /// <summary>Every interface name and description of the devices these observations resolved to.</summary>
    private async Task<InterfaceIndex> LoadInterfacesAsync(
        IReadOnlyList<NeighborObservation> observations,
        CancellationToken cancellationToken)
    {
        List<Guid> deviceIds = [.. observations
            .Select(observation => observation.RemoteDeviceId)
            .OfType<Guid>()
            .Distinct()];

        if (deviceIds.Count == 0)
        {
            return new InterfaceIndex(new Dictionary<(Guid, string), int>(), new Dictionary<(Guid, int), string>());
        }

        var rows = await context.DeviceInterfaces.AsNoTracking()
            .Where(port => deviceIds.Contains(port.DeviceId))
            .Select(port => new
            {
                port.DeviceId,
                port.IfIndex,
                port.Name,
                port.Description
            })
            .ToListAsync(cancellationToken);

        Dictionary<(Guid, string), int> byName = [];
        Dictionary<(Guid, int), string> names = [];

        foreach (var row in rows)
        {
            foreach (string? spelling in new[] { row.Name, row.Description })
            {
                if (spelling is { Length: > 0 })
                {
                    byName.TryAdd((row.DeviceId, spelling.Trim().ToLowerInvariant()), row.IfIndex);
                    names.TryAdd((row.DeviceId, row.IfIndex), spelling);
                }
            }
        }

        return new InterfaceIndex(byName, names);
    }

    /// <summary>What the far devices call their interfaces, both ways round.</summary>
    private sealed record InterfaceIndex(
        IReadOnlyDictionary<(Guid, string), int> ByName,
        IReadOnlyDictionary<(Guid, int), string> Names);

    private sealed record DeviceIndex(
        IReadOnlyDictionary<IPAddress, Guid> ByAddress,
        IReadOnlyDictionary<string, Guid> ByHostname);
}
