using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NetShield.Inventory.Persistence;

namespace NetShield.Inventory.Topology;

/// <summary>
/// Folds one device's VLAN reading into its rows in <c>device_vlans</c>, in one transaction.
/// </summary>
/// <remarks>
/// <para>
/// Simpler than <see cref="NeighborObservationApplier"/> by exactly the amount WP-2.2 is simpler
/// than WP-2.1: there is no conclusion table to reconcile, because a VLAN's estate-wide view is a
/// grouping of these rows rather than a merge of two devices' disagreeing accounts. So this adds
/// what appeared, updates what is still there, and withdraws what a complete reading no longer
/// contains — and nothing else.
/// </para>
/// <para>
/// <strong>An absence withdraws, and only a complete reading may say so.</strong>
/// <c>dot1qVlanStaticTable</c> is the device's whole current statement about the VLANs it is
/// configured with, so a VLAN it no longer lists has been removed from that switch — unlike
/// WP-1.8's ARP cache, which ages an entry out whether or not the host is still there. But a
/// device that implements neither Q-BRIDGE table was never asked a question it could answer, and
/// a reading that hit its report ceiling saw part of a table. Neither withdraws anything, which is
/// what makes ageing safe rather than destructive.
/// </para>
/// </remarks>
internal sealed class VlanObservationApplier(
    InventoryDbContext context,
    ILogger<VlanObservationApplier> logger)
{
    /// <summary>What one application changed.</summary>
    /// <param name="Added">VLANs this device was not carrying before.</param>
    /// <param name="Withdrawn">VLANs it has stopped carrying.</param>
    /// <param name="VlanCount">How many it carries now.</param>
    /// <param name="MembershipChanged">How many kept VLANs had their port list change.</param>
    internal sealed record Applied(
        int Added,
        int Withdrawn,
        int VlanCount,
        int MembershipChanged)
    {
        /// <summary>
        /// Whether the *set* moved, and therefore whether an event is worth publishing.
        /// </summary>
        /// <remarks>
        /// A port added to a VLAN the device already carried is not a change to which VLANs exist
        /// and where, which is the question <c>DeviceVlansChanged</c> answers. A subscriber that
        /// wants a port list reads one.
        /// </remarks>
        internal bool Changed => Added > 0 || Withdrawn > 0;
    }

    /// <summary>
    /// Applies one walk's VLAN reading for one device.
    /// </summary>
    /// <param name="deviceId">The device that did the reporting.</param>
    /// <param name="observations">What it reported, already normalised.</param>
    /// <param name="mayWithdraw">
    /// Whether the reading was both supported and untruncated, and may therefore withdraw a VLAN
    /// by not containing it.
    /// </param>
    /// <param name="now">One instant for the whole application, so every stamp agrees.</param>
    /// <param name="cancellationToken">The token.</param>
    public async Task<Applied> ApplyAsync(
        Guid deviceId,
        IReadOnlyList<VlanObservation> observations,
        bool mayWithdraw,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(observations);

        List<DeviceVlan> live = await context.DeviceVlans
            .Where(row => row.DeviceId == deviceId && row.WithdrawnAt == null)
            .ToListAsync(cancellationToken);

        Dictionary<int, DeviceVlan> existing = live.ToDictionary(row => row.VlanId);

        HashSet<int> reported = [];
        int added = 0;
        int membershipChanged = 0;

        foreach (VlanObservation observation in observations)
        {
            if (!reported.Add(observation.VlanId))
            {
                // One device reporting one VLAN id twice, which the current table can do across
                // two time marks. The first wins; recording both would violate the open-row
                // unique index and would double every count taken over this device.
                continue;
            }

            if (existing.TryGetValue(observation.VlanId, out DeviceVlan? row))
            {
                if (Update(row, observation, now))
                {
                    membershipChanged++;
                }
            }
            else
            {
                context.DeviceVlans.Add(Create(deviceId, observation, now));
                added++;
            }
        }

        int withdrawn = 0;

        if (mayWithdraw)
        {
            foreach (DeviceVlan row in live.Where(row => !reported.Contains(row.VlanId)))
            {
                row.WithdrawnAt = now;
                row.UpdatedAt = now;
                withdrawn++;
            }
        }

        Applied applied = new(
            added,
            withdrawn,
            live.Count(row => row.WithdrawnAt == null) + added,
            membershipChanged);

        if (applied.Changed || membershipChanged > 0)
        {
            logger.LogInformation(
                "A VLAN walk of device {DeviceId} added {Added} VLANs, withdrew {Withdrawn} "
                + "and changed the membership of {Changed}; {VlanCount} live",
                deviceId,
                applied.Added,
                applied.Withdrawn,
                applied.MembershipChanged,
                applied.VlanCount);
        }

        return applied;
    }

    private static DeviceVlan Create(Guid deviceId, VlanObservation observation, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(now),
            DeviceId = deviceId,
            VlanId = observation.VlanId,
            Name = observation.Name,
            IfIndexes = [.. observation.IfIndexes],
            UntaggedIfIndexes = [.. observation.UntaggedIfIndexes],
            PortCount = observation.PortCount,
            UnresolvedPortCount = observation.UnresolvedPortCount,
            Source = observation.Source,
            FirstDiscoveredAt = now,
            LastSeenAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

    /// <summary>
    /// Brings a live row up to date, and says whether anything but the timestamp moved.
    /// </summary>
    /// <remarks>
    /// <c>LastSeenAt</c> and <c>UpdatedAt</c> move on every walk because a confirmation is worth
    /// recording, but a walk that confirmed an unchanged VLAN has changed nothing an operator
    /// would call a change — which is what the return value keeps separable.
    /// </remarks>
    private static bool Update(DeviceVlan row, VlanObservation observation, DateTimeOffset now)
    {
        bool renamed = observation.Name is not null
            && !string.Equals(row.Name, observation.Name, StringComparison.Ordinal);

        bool changed =
            renamed
            || !row.IfIndexes.SequenceEqual(observation.IfIndexes)
            || !row.UntaggedIfIndexes.SequenceEqual(observation.UntaggedIfIndexes)
            || row.PortCount != observation.PortCount
            || row.UnresolvedPortCount != observation.UnresolvedPortCount;

        // A name is only ever replaced by another name. The fallback table carries none, so a
        // device that answered it after answering the static one would otherwise blank a VLAN's
        // name on every switch that reads it from here.
        if (observation.Name is not null)
        {
            row.Name = observation.Name;
        }

        row.IfIndexes = [.. observation.IfIndexes];
        row.UntaggedIfIndexes = [.. observation.UntaggedIfIndexes];
        row.PortCount = observation.PortCount;
        row.UnresolvedPortCount = observation.UnresolvedPortCount;
        row.Source = observation.Source ?? row.Source;
        row.LastSeenAt = now;
        row.UpdatedAt = now;

        return changed;
    }
}
