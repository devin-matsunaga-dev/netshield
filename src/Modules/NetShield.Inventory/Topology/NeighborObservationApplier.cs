using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NetShield.Contracts.Inventory;

using NetShield.Inventory.Persistence;

namespace NetShield.Inventory.Topology;

/// <summary>
/// Folds one device's topology reading into its observations and its edges, in one transaction.
/// </summary>
/// <remarks>
/// <para>
/// The shape <c>ClientObservationApplier</c> established, with the one difference that is the
/// whole of WP-2.1's "a removed link ages out" criterion: <strong>an absence here is
/// evidence</strong>. WP-1.8 closes a client binding only when a conflicting observation
/// supersedes it, because an ARP cache ages an entry out after hours whether or not the host is
/// still there. A neighbour table is not like that — it is the device's complete current
/// statement about what it can see, and its agent expires its own entries — so an entry a
/// successful reading no longer contains is a link that has gone.
/// </para>
/// <para>
/// <strong>Which readings get to say that is the careful part.</strong> Only a source that was
/// <em>supported and not truncated</em> withdraws anything. A device that does not implement CDP
/// withdraws no CDP edges, because it was never asked a question it could answer; a reading that
/// hit its report ceiling withdraws nothing either, because it saw part of a table. Those two
/// flags have been carried on every walk payload since WP-1.5 and recorded without being acted
/// on; this is the package that acts on them.
/// </para>
/// <para>
/// <strong>Edges are recomputed from every live observation, not only from this walk's.</strong>
/// The neighbour walk contributes LLDP and CDP and the route walk contributes routing, and both
/// have to converge on the same edge set — so each recomputes from all three. That is also what
/// lets the conflict rule work at all: LLDP outranking CDP on a port is a comparison between
/// sources, and a walk that only saw its own would have nothing to compare.
/// </para>
/// </remarks>
internal sealed class NeighborObservationApplier(
    InventoryDbContext context,
    TopologyResolver resolver,
    ILogger<NeighborObservationApplier> logger)
{
    /// <summary>What one application changed.</summary>
    /// <param name="Added">Edges that did not exist before this walk.</param>
    /// <param name="Withdrawn">Edges no end reports any more.</param>
    /// <param name="EdgeCount">How many live edges the device has now.</param>
    /// <param name="Suppressed">Observations the conflict rule declined to make an edge from.</param>
    internal sealed record Applied(int Added, int Withdrawn, int EdgeCount, int Suppressed)
    {
        /// <summary>Whether anything moved, and therefore whether an event is worth publishing.</summary>
        internal bool Changed => Added > 0 || Withdrawn > 0;
    }

    /// <summary>
    /// How the observing device identifies itself, where it said.
    /// </summary>
    /// <remarks>
    /// Only the neighbour walk knows this — <c>lldpLocChassisId</c> is the other half of every
    /// edge. It matters when the far device's walk lands first and creates the edge: the row's
    /// <c>B</c> end is then this device, and describing it as the identity it advertises is
    /// better than describing it by its primary key.
    /// </remarks>
    /// <param name="ChassisId">What it advertises as its identity.</param>
    /// <param name="ChassisIdKind">What kind of identifier that is.</param>
    /// <param name="SystemName">What it calls itself.</param>
    internal sealed record LocalIdentity(
        string? ChassisId,
        NeighborIdKind ChassisIdKind,
        string? SystemName);

    /// <summary>
    /// Applies one walk's observations for one device.
    /// </summary>
    /// <param name="deviceId">The device that did the observing.</param>
    /// <param name="identity">How that device identifies itself, where the walk established it.</param>
    /// <param name="observations">What it reported, already normalised.</param>
    /// <param name="handledSources">
    /// The sources this walk is responsible for. Observations of other sources are left exactly
    /// as they are — which is what keeps a route walk from touching an LLDP edge.
    /// </param>
    /// <param name="completeSources">
    /// The subset of those whose reading was both supported and untruncated, and which may
    /// therefore withdraw an observation by not containing it.
    /// </param>
    /// <param name="now">One instant for the whole application, so every stamp agrees.</param>
    /// <param name="cancellationToken">The token.</param>
    public async Task<Applied> ApplyAsync(
        Guid deviceId,
        LocalIdentity identity,
        IReadOnlyList<NeighborObservation> observations,
        IReadOnlySet<NeighborSource> handledSources,
        IReadOnlySet<NeighborSource> completeSources,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(handledSources);
        ArgumentNullException.ThrowIfNull(completeSources);

        IReadOnlyList<NeighborObservation> resolved =
            await resolver.ResolveAsync(observations, cancellationToken);

        List<DeviceNeighbor> live = await context.DeviceNeighbors
            .Where(row => row.DeviceId == deviceId && row.WithdrawnAt == null)
            .ToListAsync(cancellationToken);

        Dictionary<(NeighborSource, int, string), DeviceNeighbor> existing = [];

        foreach (DeviceNeighbor row in live)
        {
            existing[(row.Source, row.LocalIfIndex, row.RemoteKey)] = row;
        }

        HashSet<(NeighborSource, int, string)> reported = [];

        foreach (NeighborObservation observation in resolved)
        {
            var key = (observation.Source, observation.LocalIfIndex, observation.RemoteKey);

            if (!reported.Add(key))
            {
                // One device advertising the same far end twice on one port, which happens when
                // a neighbour has two entries that differ only in a field NetShield does not key
                // by. The first wins; recording both would violate the open-row unique index.
                continue;
            }

            if (existing.TryGetValue(key, out DeviceNeighbor? row))
            {
                Update(row, observation, now);
            }
            else
            {
                DeviceNeighbor created = Create(deviceId, observation, now);

                context.DeviceNeighbors.Add(created);
                live.Add(created);
                existing[key] = created;
            }
        }

        int withdrawnObservations = 0;

        foreach (DeviceNeighbor row in live)
        {
            bool ours = handledSources.Contains(row.Source);
            bool mayWithdraw = completeSources.Contains(row.Source);
            bool stillReported = reported.Contains((row.Source, row.LocalIfIndex, row.RemoteKey));

            if (!ours || !mayWithdraw || stillReported)
            {
                continue;
            }

            row.WithdrawnAt = now;
            row.UpdatedAt = now;
            withdrawnObservations++;
        }

        Applied applied = await ReconcileAsync(
            deviceId,
            identity,
            [.. live.Where(row => row.WithdrawnAt == null)],
            now,
            cancellationToken);

        if (withdrawnObservations > 0)
        {
            logger.LogInformation(
                "A topology walk of device {DeviceId} withdrew {Count} neighbour observations",
                deviceId,
                withdrawnObservations);
        }

        return applied;
    }

    /// <summary>
    /// Turns this device's live observations into edges, and lets go of the ones it no longer
    /// supports.
    /// </summary>
    private async Task<Applied> ReconcileAsync(
        Guid deviceId,
        LocalIdentity identity,
        IReadOnlyList<DeviceNeighbor> live,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        Dictionary<string, List<DeviceNeighbor>> surviving = Survivors(live, out int suppressed);

        List<DeviceAdjacency> edges = await context.DeviceAdjacencies
            .Where(edge => edge.WithdrawnAt == null
                && (edge.ADeviceId == deviceId || edge.BDeviceId == deviceId))
            .ToListAsync(cancellationToken);

        Dictionary<string, DeviceAdjacency> byKey = [];

        foreach (DeviceAdjacency edge in edges)
        {
            byKey[KeyFor(edge, deviceId)] = edge;
        }

        HashSet<string> supported = [];
        HashSet<Guid> supporting = [];
        int added = 0;

        foreach ((string edgeKey, List<DeviceNeighbor> group) in surviving)
        {
            Endpoints endpoints = Endpoints.For(deviceId, identity, Preferred(group));

            supported.Add(edgeKey);

            if (!byKey.TryGetValue(edgeKey, out DeviceAdjacency? edge))
            {
                edge = New(endpoints, now);

                context.DeviceAdjacencies.Add(edge);
                byKey[edgeKey] = edge;
                added++;
            }

            Describe(edge, endpoints, group, deviceId, now);

            foreach (DeviceNeighbor observation in group)
            {
                observation.AdjacencyId = edge.Id;
                supporting.Add(observation.Id);
            }
        }

        // Every observation the conflict rule set aside points at no edge. It is a live row and
        // it keeps its history — it simply is not the reason NetShield believes anything.
        foreach (DeviceNeighbor observation in live)
        {
            if (!supporting.Contains(observation.Id))
            {
                observation.AdjacencyId = null;
            }
        }

        int withdrawn = 0;

        foreach ((string edgeKey, DeviceAdjacency edge) in byKey)
        {
            if (supported.Contains(edgeKey))
            {
                continue;
            }

            // This device no longer reports the edge. The far end may still, which is why the
            // flags are per side and the row survives with one of them cleared — one switch
            // going quiet downgrades a link rather than deleting it.
            if (edge.ADeviceId == deviceId)
            {
                edge.ObservedFromA = false;
                edge.SourcesA = [];
            }
            else
            {
                edge.ObservedFromB = false;
                edge.SourcesB = [];
            }

            edge.UpdatedAt = now;

            if (!edge.ObservedFromA && !edge.ObservedFromB)
            {
                edge.WithdrawnAt = now;
                withdrawn++;
            }
            else
            {
                edge.Confidence = AdjacencyRule.Confidence(Sources(edge), Bidirectional(edge));
            }
        }

        int edgeCount = byKey.Values.Count(edge => edge.WithdrawnAt == null);

        return new Applied(added, withdrawn, edgeCount, suppressed);
    }

    /// <summary>
    /// The observations that survive the conflict rule, grouped by the edge they support.
    /// </summary>
    /// <remarks>
    /// Grouped by the local port as well as by the far end, because two links to one device from
    /// two interfaces are two edges — an aggregate collapsed into one would lose a cable.
    /// </remarks>
    private static Dictionary<string, List<DeviceNeighbor>> Survivors(
        IReadOnlyList<DeviceNeighbor> live,
        out int suppressed)
    {
        Dictionary<string, List<DeviceNeighbor>> surviving = [];
        int set = 0;

        foreach (IGrouping<int, DeviceNeighbor> port in live.GroupBy(row => row.LocalIfIndex))
        {
            List<DeviceNeighbor> onPort = [.. port];

            IReadOnlySet<string> keys = AdjacencyRule.Survivors(
                [.. onPort.Select(row => new AdjacencyRule.Candidate(
                    row.Source,
                    Key(row),
                    row.RemoteDeviceId is not null))]);

            foreach (DeviceNeighbor row in onPort)
            {
                string key = Key(row);

                if (!keys.Contains(key))
                {
                    set++;

                    continue;
                }

                string edgeKey = $"{row.LocalIfIndex}|{key}";

                if (!surviving.TryGetValue(edgeKey, out List<DeviceNeighbor>? group))
                {
                    group = [];
                    surviving[edgeKey] = group;
                }

                group.Add(row);
            }
        }

        suppressed = set;

        return surviving;
    }

    /// <summary>
    /// Which observation of a group describes the edge.
    /// </summary>
    /// <remarks>
    /// They agree about the far end — that is what put them in one group — and can disagree about
    /// how to spell it. LLDP first, then CDP, then routing, then the identifier itself: the same
    /// order of trust the conflict rule uses, applied to a question that is only about
    /// presentation. Deterministic, so re-running a walk cannot make an unchanged edge look
    /// edited.
    /// </remarks>
    private static DeviceNeighbor Preferred(List<DeviceNeighbor> group) =>
        group
            .OrderBy(row => row.Source switch
            {
                NeighborSource.Lldp => 0,
                NeighborSource.Cdp => 1,
                _ => 2
            })
            .ThenBy(row => row.RemoteChassisId, StringComparer.Ordinal)
            .First();

    private static void Describe(
        DeviceAdjacency edge,
        Endpoints endpoints,
        List<DeviceNeighbor> group,
        Guid deviceId,
        DateTimeOffset now)
    {
        if (edge.ADeviceId == deviceId)
        {
            // This device is the A end, so it is the one that can see B. Everything about the far
            // end is written from what it just observed.
            edge.ObservedFromA = true;
            edge.SourcesA = Names(group);
            edge.AInterfaceName = endpoints.AInterfaceName ?? edge.AInterfaceName;
            edge.BDeviceId = endpoints.B.DeviceId ?? edge.BDeviceId;
            edge.BIfIndex = endpoints.B.IfIndex ?? edge.BIfIndex;
            edge.BInterfaceName = endpoints.B.InterfaceName ?? edge.BInterfaceName;
            edge.BChassisId = endpoints.B.ChassisId;
            edge.BChassisIdKind = endpoints.B.ChassisIdKind;
            edge.BPortId = endpoints.B.PortId ?? edge.BPortId;
            edge.BSystemName = endpoints.B.SystemName ?? edge.BSystemName;
        }
        else
        {
            // This device is the B end. It describes its own interface and the A end's, and it
            // does not overwrite the B description — the A device wrote that from what it saw,
            // which is a better account of this device than this device's own guess at how it
            // looks from over there.
            edge.ObservedFromB = true;
            edge.SourcesB = Names(group);
            edge.BInterfaceName = endpoints.B.InterfaceName ?? edge.BInterfaceName;
            edge.AInterfaceName = endpoints.AInterfaceName ?? edge.AInterfaceName;
        }

        edge.Confidence = AdjacencyRule.Confidence(Sources(edge), Bidirectional(edge));
        edge.LastSeenAt = now;
        edge.UpdatedAt = now;
    }

    private static DeviceAdjacency New(Endpoints endpoints, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(now),
            ADeviceId = endpoints.ADeviceId,
            AIfIndex = endpoints.AIfIndex,
            AInterfaceName = endpoints.AInterfaceName,
            BDeviceId = endpoints.B.DeviceId,
            BIfIndex = endpoints.B.IfIndex,
            BInterfaceName = endpoints.B.InterfaceName,
            BChassisId = endpoints.B.ChassisId,
            BChassisIdKind = endpoints.B.ChassisIdKind,
            BPortId = endpoints.B.PortId,
            BSystemName = endpoints.B.SystemName,
            AdjacencyKey = endpoints.AdjacencyKey,
            Confidence = AdjacencyConfidence.Possible,
            FirstDiscoveredAt = now,
            LastSeenAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

    private static DeviceNeighbor Create(
        Guid deviceId,
        NeighborObservation observation,
        DateTimeOffset now)
    {
        DeviceNeighbor row = new()
        {
            Id = Guid.CreateVersion7(now),
            DeviceId = deviceId,
            Source = observation.Source,
            LocalIfIndex = observation.LocalIfIndex,
            RemoteChassisId = Trim(observation.RemoteChassisId, TopologyLimits.IdentifierLength)!,
            RemoteChassisIdKind = observation.RemoteChassisIdKind,
            RemoteKey = Trim(observation.RemoteKey, TopologyLimits.KeyLength)!,
            FirstDiscoveredAt = now,
            CreatedAt = now
        };

        Update(row, observation, now);

        return row;
    }

    private static void Update(
        DeviceNeighbor row,
        NeighborObservation observation,
        DateTimeOffset now)
    {
        row.LocalInterfaceName = Trim(observation.LocalInterfaceName, TopologyLimits.NameLength);
        row.RemotePortId = Trim(observation.RemotePortId, TopologyLimits.PortIdLength);
        row.RemotePortIdKind = observation.RemotePortIdKind;
        row.RemotePortDescription = Trim(observation.RemotePortDescription, TopologyLimits.NameLength);
        row.RemoteSystemName = Trim(observation.RemoteSystemName, TopologyLimits.NameLength);
        row.RemoteSystemDescription =
            Trim(observation.RemoteSystemDescription, TopologyLimits.DescriptionLength);
        row.RemotePlatform = Trim(observation.RemotePlatform, TopologyLimits.NameLength);
        row.RemoteManagementAddress = observation.RemoteManagementAddress;
        row.RemoteDeviceId = observation.RemoteDeviceId;
        row.RemoteIfIndex = observation.RemoteIfIndex;
        row.RemoteInterfaceName = Trim(observation.RemoteInterfaceName, TopologyLimits.NameLength);
        row.AdjacencyKey = Trim(observation.AdjacencyKey, TopologyLimits.KeyLength)!;
        row.Capabilities = observation.Capabilities;
        row.EvidenceCount = observation.EvidenceCount;
        row.LastSeenAt = now;
        row.UpdatedAt = now;
    }

    /// <summary>
    /// The value, cut to what the column holds.
    /// </summary>
    /// <remarks>
    /// A device advertises whatever its operator typed and a <c>sysDescr</c> is routinely a
    /// paragraph. Losing the tail of one is a smaller harm than failing the walk that carried the
    /// edge it came with.
    /// </remarks>
    private static string? Trim(string? value, int length) =>
        value is null || value.Length <= length ? value : value[..length];

    private static string Key(DeviceNeighbor row) =>
        RemoteIdentity.Adjacency(row.RemoteDeviceId, row.RemoteKey);

    /// <summary>
    /// The key one device finds an existing edge by — the same string
    /// <see cref="Endpoints.KeyFrom"/> computes for a new one.
    /// </summary>
    private static string KeyFor(DeviceAdjacency edge, Guid deviceId) =>
        edge.ADeviceId == deviceId
            ? $"{edge.AIfIndex}|{edge.AdjacencyKey}"
            : $"{edge.BIfIndex}|{RemoteIdentity.DevicePrefix}{edge.ADeviceId}";

    private static IReadOnlyList<string> Names(List<DeviceNeighbor> group) =>
        [.. group.Select(row => row.Source)
            .Distinct()
            .OrderBy(source => source)
            .Select(source => source.ToString())];

    private static IReadOnlySet<NeighborSource> Sources(DeviceAdjacency edge)
    {
        HashSet<NeighborSource> sources = [];

        foreach (string name in edge.SourcesA.Concat(edge.SourcesB))
        {
            if (Enum.TryParse(name, out NeighborSource source))
            {
                sources.Add(source);
            }
        }

        return sources;
    }

    private static bool Bidirectional(DeviceAdjacency edge) =>
        edge.ObservedFromA && edge.ObservedFromB;

    /// <summary>
    /// The two ends of one edge, in the order they will be stored in.
    /// </summary>
    /// <remarks>
    /// See <see cref="AdjacencyRule.ShouldSwap"/> for why the swap happens only when the far end
    /// is a device <em>and</em> its interface resolved.
    /// </remarks>
    /// <param name="ADeviceId">The lower-ordered end. Always a device — somebody observed this.</param>
    /// <param name="AIfIndex">Its interface.</param>
    /// <param name="AInterfaceName">What it calls that interface.</param>
    /// <param name="B">The other end, which need not be a device NetShield monitors.</param>
    /// <param name="AdjacencyKey">How <c>B</c> is identified relative to <c>A</c>.</param>
    private sealed record Endpoints(
        Guid ADeviceId,
        int AIfIndex,
        string? AInterfaceName,
        Endpoint B,
        string AdjacencyKey)
    {
        internal static Endpoints For(
            Guid deviceId,
            LocalIdentity identity,
            DeviceNeighbor observation)
        {
            bool swap = AdjacencyRule.ShouldSwap(
                deviceId,
                observation.LocalIfIndex,
                observation.RemoteDeviceId,
                observation.RemoteIfIndex);

            if (!swap)
            {
                return new Endpoints(
                    deviceId,
                    observation.LocalIfIndex,
                    observation.LocalInterfaceName,
                    new Endpoint(
                        observation.RemoteDeviceId,
                        observation.RemoteIfIndex,
                        observation.RemoteInterfaceName,
                        observation.RemoteChassisId,
                        observation.RemoteChassisIdKind,
                        observation.RemotePortId,
                        observation.RemoteSystemName),
                    Key(observation));
            }

            // The far device sorts lower, so it becomes A and this device becomes B. Its own
            // advertised identity is what describes it — a fallback of its id is never empty,
            // is never mistaken for something a device advertised, and is only ever reached when
            // this device speaks no LLDP and the far one created the row first.
            return new Endpoints(
                observation.RemoteDeviceId!.Value,
                observation.RemoteIfIndex!.Value,
                observation.RemoteInterfaceName,
                new Endpoint(
                    deviceId,
                    observation.LocalIfIndex,
                    observation.LocalInterfaceName,
                    identity.ChassisId is { Length: > 0 } advertised
                        ? advertised
                        : RemoteIdentity.DevicePrefix + deviceId,
                    identity.ChassisId is { Length: > 0 }
                        ? identity.ChassisIdKind
                        : NeighborIdKind.Unknown,
                    observation.LocalInterfaceName,
                    identity.SystemName),
                RemoteIdentity.DevicePrefix + deviceId);
        }
    }

    /// <summary>One end of an edge. The <c>A</c> end is always a device; the <c>B</c> end need not be.</summary>
    private sealed record Endpoint(
        Guid? DeviceId,
        int? IfIndex,
        string? InterfaceName,
        string ChassisId,
        NeighborIdKind ChassisIdKind,
        string? PortId,
        string? SystemName);
}
