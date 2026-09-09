using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NetShield.Contracts.Collector;
using NetShield.Contracts.Collector.Events;
using NetShield.Contracts.Inventory;
using NetShield.Contracts.Inventory.Events;

using NetShield.Inventory.Collector;
using NetShield.Inventory.Persistence;

using NetShield.Platform.Messaging;

namespace NetShield.Inventory.Topology.Handlers;

/// <summary>
/// Reads a finished VLAN walk and records which VLANs a device is configured with.
/// </summary>
/// <remarks>
/// <para>
/// The seventh subscriber to <c>CollectorJobCompleted</c>. Like the six before it, it reads only
/// the rows this package queued — a <c>Discover</c> whose parameters name the <c>vlans</c> walk —
/// and leaves every other job to whoever queued it.
/// </para>
/// <para>
/// <strong>A failed walk changes no VLAN.</strong> An unreachable device, a refused community
/// string or a timeout means nothing was observed, and letting that withdraw would report an
/// estate whose VLANs vanished because one switch stopped answering SNMP. The same reasoning
/// WP-1.4 applied to device state, WP-1.5 to device identity, WP-1.8 to client bindings and
/// WP-2.1 to edges: a collector's health is never evidence about the estate.
/// </para>
/// <para>
/// <strong>A device with no VLAN table is not a device with no VLANs.</strong> The payload carries
/// a <c>supported</c> flag for exactly this — a router implements neither Q-BRIDGE table — and
/// only a reading that was supported <em>and</em> untruncated is allowed to withdraw a VLAN by not
/// mentioning it.
/// </para>
/// </remarks>
internal sealed class RecordVlanWalkResultHandler(
    InventoryDbContext context,
    TopologyWalkResultReader reader,
    VlanObservationApplier applier,
    OutboxEnlistment outbox,
    IOptions<TopologyOptions> options,
    ILogger<RecordVlanWalkResultHandler> logger) : IIntegrationEventHandler<CollectorJobCompleted>
{
    public async Task HandleAsync(
        CollectorJobCompleted integrationEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        TopologyWalkResultReader.Match? matched = await reader.MatchAsync(
            integrationEvent,
            TopologyWalkKind.Vlans,
            options.Value,
            cancellationToken);

        if (matched is not { } match)
        {
            return;
        }

        DeviceTopologyScan scan = match.Scan;

        scan.LastVlanJobId = integrationEvent.JobId;
        scan.LastVlanWalkAt = match.Now;
        scan.UpdatedAt = match.Now;

        VlanObservationApplier.Applied? applied = null;

        if (integrationEvent.Outcome != CollectorJobOutcome.Succeeded)
        {
            // Already through SecretRedactor on its way into the column (WP-1.3), so it is safe
            // to carry across and safe to log.
            scan.LastVlanError =
                match.Job.Detail ?? "The collector could not read the device's VLAN tables.";

            logger.LogWarning(
                "A VLAN walk of device {DeviceId} could not be performed: {Detail}",
                match.DeviceId,
                scan.LastVlanError);
        }
        else if (Parse(match.Job) is { } result)
        {
            applied = await ApplyAsync(scan, result, match, cancellationToken);
        }
        else
        {
            scan.LastVlanError = "The collector reported a result this walk could not read.";

            logger.LogWarning(
                "Collector job {JobId} succeeded but carried no readable VLAN walk result.",
                integrationEvent.JobId);
        }

        await context.SaveChangesAsync(cancellationToken);

        if (applied is { Changed: true })
        {
            await PublishAsync(match, applied, cancellationToken);
        }
    }

    private async Task<VlanObservationApplier.Applied?> ApplyAsync(
        DeviceTopologyScan scan,
        VlanWalkResult result,
        TopologyWalkResultReader.Match match,
        CancellationToken cancellationToken)
    {
        if (!await reader.IsLiveAsync(match.DeviceId, cancellationToken))
        {
            logger.LogInformation(
                "A VLAN walk result for device {DeviceId} was dropped: the device has been removed.",
                match.DeviceId);

            return null;
        }

        scan.LastVlanError = null;
        scan.VlansSupported = result.VlansSupported;
        scan.VlanTable = result.VlansSupported ? Table(result.VlanTable) : null;
        scan.LastVlanCount = result.VlansSupported ? result.VlanCount : null;

        // A device that answered nothing has said nothing, so it may not withdraw; nor may a
        // reading cut short by the report ceiling, which saw part of a table.
        bool mayWithdraw = result.VlansSupported && !result.VlansTruncated;

        if (result.VlansTruncated)
        {
            logger.LogWarning(
                "A VLAN walk of device {DeviceId} hit its reporting ceiling at {Count} VLANs. "
                + "No VLAN will be withdrawn from a truncated reading.",
                match.DeviceId,
                result.VlanCount);
        }

        return await applier.ApplyAsync(
            match.DeviceId,
            [.. Observations(result)],
            mayWithdraw,
            match.Now,
            cancellationToken);
    }

    /// <summary>
    /// The entries that name a VLAN, normalised.
    /// </summary>
    /// <remarks>
    /// An entry whose id is outside 802.1Q's <c>1..4094</c> is dropped: the collector already
    /// refuses those, and checking again here is what keeps the rule with the table that enforces
    /// it rather than only with the process that reads a device. Port lists are sorted,
    /// de-duplicated and cut to <c>TopologyLimits.MaxPortsPerVlan</c>, and the untagged list is
    /// intersected with the member list so the invariant the contract states — a subset — is true
    /// of the row rather than merely of a well-behaved agent.
    /// </remarks>
    private static IEnumerable<VlanObservation> Observations(VlanWalkResult result)
    {
        foreach (VlanEntry entry in result.Vlans ?? [])
        {
            if (entry.VlanId < TopologyLimits.MinVlanId || entry.VlanId > TopologyLimits.MaxVlanId)
            {
                continue;
            }

            int[] members = Ports(entry.IfIndexes);
            int[] untagged = [.. Ports(entry.UntaggedIfIndexes).Intersect(members)];

            yield return new VlanObservation
            {
                VlanId = entry.VlanId,
                Name = Blank(entry.Name),
                IfIndexes = members,
                UntaggedIfIndexes = untagged,
                PortCount = Math.Max(entry.PortCount, 0),
                UnresolvedPortCount = Math.Max(entry.UnresolvedPortCount, 0),
                Source = Table(result.VlanTable)
            };
        }
    }

    private static int[] Ports(IReadOnlyList<int>? indexes) =>
        [.. (indexes ?? []).Where(index => index > 0)
            .Distinct()
            .Order()
            .Take(TopologyLimits.MaxPortsPerVlan)];

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim()[..Math.Min(value.Trim().Length, TopologyLimits.NameLength)];

    private static string? Table(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim()[..Math.Min(value.Trim().Length, TopologyLimits.TableNameLength)];

    private async Task PublishAsync(
        TopologyWalkResultReader.Match match,
        VlanObservationApplier.Applied applied,
        CancellationToken cancellationToken)
    {
        string? hostname = await context.Devices.AsNoTracking()
            .Where(device => device.Id == match.DeviceId)
            .Select(device => device.Hostname)
            .SingleOrDefaultAsync(cancellationToken);

        if (hostname is null)
        {
            return;
        }

        // Staged on this context and saved with it, so the VLAN rows and the event announcing
        // them commit together or not at all — the OutboxEnlistment arrangement WP-1.1 settled.
        outbox.Enlist(context, new DeviceVlansChanged(
            match.DeviceId,
            hostname,
            applied.VlanCount,
            applied.Added,
            applied.Withdrawn,
            match.Now));

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>The walk result on the job row, or nothing if it does not read as one.</summary>
    private VlanWalkResult? Parse(CollectorJob job)
    {
        if (string.IsNullOrEmpty(job.Result))
        {
            return null;
        }

        try
        {
            VlanWalkResult? result = JsonSerializer.Deserialize(
                job.Result,
                TopologySerializerContext.Default.VlanWalkResult);

            // The discriminator has to agree with the parameters. A payload that does not name
            // this walk is a collector answering a question nobody asked.
            return string.Equals(result?.Walk, VlanWalkParameters.WalkName, StringComparison.Ordinal)
                ? result
                : null;
        }
        catch (JsonException exception)
        {
            logger.LogWarning(
                exception,
                "Collector job {JobId} carried a result that is not a VLAN walk result.",
                job.Id);

            return null;
        }
    }
}
