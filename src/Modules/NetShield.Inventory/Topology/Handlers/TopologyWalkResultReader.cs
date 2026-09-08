using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using NetShield.Contracts.Collector;
using NetShield.Contracts.Collector.Events;
using NetShield.Contracts.Inventory;

using NetShield.Inventory.Collector;
using NetShield.Inventory.Persistence;

using NetShield.Platform.Time;

namespace NetShield.Inventory.Topology.Handlers;

/// <summary>
/// The half of a topology result handler that is the same for both walks: recognising a job as
/// this package's, finding the scan row, and refusing a redelivery.
/// </summary>
/// <remarks>
/// <para>
/// Written once because there are two handlers and the alternative is the same forty lines in
/// each, differing in a discriminator. It deliberately does not try to unify the halves that are
/// genuinely different — what a payload means, and which sources it may withdraw — because those
/// are the two places the walks disagree and hiding that behind a shared abstraction is how one
/// walk quietly starts answering the other's question.
/// </para>
/// <para>
/// <strong>Safe to run twice.</strong> Outbox delivery is at-least-once, and the scan row records
/// the job each walk last applied, in its own column. A redelivered walk is dropped rather than
/// folded in again, which matters here for the reason it mattered for client bindings: a second
/// application arriving after the far device had been walked would withdraw edges the first
/// application's evidence had already been spent on.
/// </para>
/// </remarks>
internal sealed class TopologyWalkResultReader(InventoryDbContext context, IClock clock)
{
    /// <summary>What a completed job turned out to be, when it is one of this package's.</summary>
    /// <param name="Job">The job row, with its parameters and result.</param>
    /// <param name="Scan">The device's scan row, created if this is the first walk of it.</param>
    /// <param name="DeviceId">The device that was walked.</param>
    /// <param name="Now">One instant for the whole application.</param>
    internal sealed record Match(
        CollectorJob Job,
        DeviceTopologyScan Scan,
        Guid DeviceId,
        DateTimeOffset Now);

    /// <summary>
    /// Whether this completed job is a topology walk of the given kind that has not been applied.
    /// </summary>
    public async Task<Match?> MatchAsync(
        CollectorJobCompleted integrationEvent,
        TopologyWalkKind walk,
        TopologyOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        ArgumentNullException.ThrowIfNull(options);

        if (integrationEvent.Kind != CollectorJobKind.Discover
            || integrationEvent.DeviceId is not { } deviceId)
        {
            return null;
        }

        CollectorJob? job = await context.CollectorJobs.AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == integrationEvent.JobId,
                cancellationToken);

        if (job is null || !Names(job, walk))
        {
            return null;
        }

        DateTimeOffset now = clock.UtcNow;

        DeviceTopologyScan scan = await context.DeviceTopologyScans
            .SingleOrDefaultAsync(row => row.DeviceId == deviceId, cancellationToken)
            ?? Create(deviceId, options, now);

        Guid? applied = walk == TopologyWalkKind.Routes
            ? scan.LastRouteJobId
            : scan.LastNeighborJobId;

        if (applied == integrationEvent.JobId)
        {
            return null;
        }

        return new Match(job, scan, deviceId, now);
    }

    /// <summary>Whether the device is still one NetShield monitors.</summary>
    /// <remarks>
    /// A device removed while its walk was in flight has edges that describe an estate it is no
    /// longer part of. Writing them would resurrect it in every join that forgot to filter, which
    /// is the same reason a client walk drops its result for a removed device.
    /// </remarks>
    public Task<bool> IsLiveAsync(Guid deviceId, CancellationToken cancellationToken) =>
        context.Devices.AnyAsync(
            device => device.Id == deviceId && device.DeletedAt == null,
            cancellationToken);

    /// <summary>Whether this job's parameters name the walk in question.</summary>
    private static bool Names(CollectorJob job, TopologyWalkKind walk)
    {
        if (string.IsNullOrEmpty(job.Parameters))
        {
            return false;
        }

        string expected = walk == TopologyWalkKind.Routes
            ? RouteWalkParameters.WalkName
            : NeighborWalkParameters.WalkName;

        try
        {
            using JsonDocument document = JsonDocument.Parse(job.Parameters);

            return document.RootElement.TryGetProperty("walk", out JsonElement name)
                && name.ValueKind == JsonValueKind.String
                && string.Equals(name.GetString(), expected, StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            // Another package's parameter document, shaped differently. Not ours.
            return false;
        }
    }

    private DeviceTopologyScan Create(Guid deviceId, TopologyOptions options, DateTimeOffset now)
    {
        DeviceTopologyScan row = new()
        {
            Id = Guid.CreateVersion7(now),
            DeviceId = deviceId,
            NextNeighborWalkAt = now.AddSeconds(options.NeighborWalkIntervalSeconds),
            NextRouteWalkAt = now.AddSeconds(options.RouteWalkIntervalSeconds),
            CreatedAt = now,
            UpdatedAt = now
        };

        context.DeviceTopologyScans.Add(row);

        return row;
    }
}
