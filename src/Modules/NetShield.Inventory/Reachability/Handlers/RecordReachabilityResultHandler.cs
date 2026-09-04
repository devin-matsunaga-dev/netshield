using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NetShield.Contracts.Collector;
using NetShield.Contracts.Collector.Events;
using NetShield.Contracts.Inventory;
using NetShield.Contracts.Inventory.Events;

using NetShield.Inventory.Collector;
using NetShield.Inventory.Devices;
using NetShield.Inventory.Persistence;

using NetShield.Platform.Messaging;
using NetShield.Platform.Time;

namespace NetShield.Inventory.Reachability.Handlers;

/// <summary>
/// Reads a finished reachability probe and moves the device's state if the evidence warrants it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is the first subscriber to <c>CollectorJobCompleted</c>.</strong> WP-1.3 stored
/// every result and interpreted none of them, leaving the event as the seam each collection
/// package would hang from; this is that seam being used. It reads only the rows this package
/// queued — a <c>Poll</c> whose parameters name the ICMP probe — and leaves every other job to
/// whoever queued it, because a <c>Poll</c> row from the SNMP polling in Phase 3 will sit in the
/// same table and look the same from the outside.
/// </para>
/// <para>
/// <strong>Safe to run twice.</strong> Outbox delivery is at-least-once, and every counter this
/// touches is exactly the kind that a redelivery would quietly corrupt: applying one probe twice
/// would advance a run halfway to a threshold that only one probe actually supports. The
/// reachability row records the last job it applied, and a result for that job is dropped.
/// </para>
/// <para>
/// <strong>A collector failure is not a device observation.</strong> A job whose outcome is
/// <see cref="CollectorJobOutcome.Failed"/> means the probe could not be performed — no ICMP
/// socket, an address the collector could not use, a timeout inside its own process — and it is
/// recorded on the row as the collector's problem without touching the device's state or its run
/// of observations. The alternative marks five hundred devices offline the moment one process
/// loses a capability, which is an alert about the wrong thing.
/// </para>
/// </remarks>
internal sealed class RecordReachabilityResultHandler(
    InventoryDbContext context,
    OutboxEnlistment outbox,
    IOptions<ReachabilityOptions> options,
    IClock clock,
    ILogger<RecordReachabilityResultHandler> logger) : IIntegrationEventHandler<CollectorJobCompleted>
{
    public async Task HandleAsync(
        CollectorJobCompleted integrationEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        if (integrationEvent.Kind != CollectorJobKind.Poll || integrationEvent.DeviceId is not { } deviceId)
        {
            return;
        }

        CollectorJob? job = await context.CollectorJobs.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == integrationEvent.JobId, cancellationToken);

        if (job is null || !IsIcmpProbe(job))
        {
            return;
        }

        DeviceReachability? reachability = await context.DeviceReachabilities
            .SingleOrDefaultAsync(row => row.DeviceId == deviceId, cancellationToken);

        if (reachability is null)
        {
            // The scheduler creates the row when it queues the probe, so the only way here is a
            // device removed and its row cleaned up between the two — or a job somebody queued
            // by hand. Neither is worth inventing a row for: nothing schedules against a row
            // this handler created, so it would be a row that never updates again.
            logger.LogInformation(
                "A reachability result for device {DeviceId} was dropped: the device has no reachability row.",
                deviceId);

            return;
        }

        if (reachability.LastAppliedJobId == integrationEvent.JobId)
        {
            return;
        }

        DateTimeOffset now = clock.UtcNow;

        reachability.LastAppliedJobId = integrationEvent.JobId;
        reachability.UpdatedAt = now;

        if (integrationEvent.Outcome != CollectorJobOutcome.Succeeded)
        {
            RecordCollectorFailure(reachability, job, deviceId);
        }
        else if (Parse(job) is { } result)
        {
            await ApplyObservationAsync(reachability, result, deviceId, now, cancellationToken);
        }
        else
        {
            // A successful job whose payload is not a probe result is a collector reporting
            // something this package cannot read. It is the collector's problem, recorded the
            // same way, and specifically not evidence that the device is down.
            reachability.LastError = "The collector reported a result this probe could not read.";

            logger.LogWarning(
                "Collector job {JobId} succeeded but carried no readable ICMP probe result.",
                integrationEvent.JobId);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Folds a probe that ran into the device's run of observations, and publishes a transition
    /// if one is warranted.
    /// </summary>
    private async Task ApplyObservationAsync(
        DeviceReachability reachability,
        IcmpProbeResult result,
        Guid deviceId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        reachability.LastError = null;
        reachability.LastProbeAt = now;
        reachability.LastLossPercent = result.LossPercent;
        reachability.LastRttMilliseconds = result.RttMillisecondsAvg;

        Device? device = await context.Devices
            .SingleOrDefaultAsync(candidate => candidate.Id == deviceId && candidate.DeletedAt == null, cancellationToken);

        if (device is null)
        {
            // Removed while the probe was in flight. The observation is still recorded above —
            // it is what was seen — but there is no device left to publish a state for.
            return;
        }

        DeviceState observed = ReachabilityStateMachine.Classify(result.Sent, result.Received);

        ReachabilityTransition transition = ReachabilityStateMachine.Apply(
            device.State,
            reachability.PendingState,
            reachability.PendingObservations,
            observed,
            options.Value);

        reachability.PendingState = transition.PendingState;
        reachability.PendingObservations = transition.PendingObservations;

        if (!transition.Changed)
        {
            return;
        }

        DeviceState previous = device.State;

        device.State = transition.State;

        // Deliberately not device.UpdatedAt. Nothing an operator maintains about this device has
        // changed, and stamping it would make every probe look like an estate-wide edit in the
        // device list, which sorts by it. When the state moved is on the reachability row.
        reachability.LastChangedAt = now;

        logger.LogInformation(
            "Device {DeviceId} moved from {PreviousState} to {State} after {Observations} consecutive observations.",
            deviceId,
            previous,
            transition.State,
            transition.PendingObservations);

        // The event and the two rows commit together, so nothing can subscribe to a transition
        // that was rolled back and no transition can be recorded without one (ARCHITECTURE.md §5).
        outbox.Enlist(
            context,
            new DeviceStateChanged(deviceId, device.Hostname, previous, transition.State, now));
    }

    /// <summary>
    /// Records that the probe did not run, without letting it look like the device answering.
    /// </summary>
    private void RecordCollectorFailure(DeviceReachability reachability, CollectorJob job, Guid deviceId)
    {
        // The job's detail has already been through SecretRedactor on its way into the column
        // (WP-1.3), so it is safe to carry across and safe to log.
        reachability.LastError = job.Detail ?? "The collector could not perform the probe.";

        logger.LogWarning(
            "A reachability probe for device {DeviceId} could not be performed: {Detail}",
            deviceId,
            reachability.LastError);
    }

    /// <summary>Whether this job is one this package queued.</summary>
    private static bool IsIcmpProbe(CollectorJob job)
    {
        if (string.IsNullOrEmpty(job.Parameters))
        {
            return false;
        }

        try
        {
            IcmpProbeParameters? parameters = JsonSerializer.Deserialize(
                job.Parameters,
                ReachabilitySerializerContext.Default.IcmpProbeParameters);

            return string.Equals(parameters?.Probe, IcmpProbeParameters.ProbeName, StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            // Another package's parameter document, shaped differently. Not ours.
            return false;
        }
    }

    /// <summary>The probe result on the job row, or nothing if it does not read as one.</summary>
    private IcmpProbeResult? Parse(CollectorJob job)
    {
        if (string.IsNullOrEmpty(job.Result))
        {
            return null;
        }

        try
        {
            IcmpProbeResult? result = JsonSerializer.Deserialize(
                job.Result,
                ReachabilitySerializerContext.Default.IcmpProbeResult);

            // Sent is what the classification is a proportion of. A payload that claims to have
            // sent nothing observed nothing, whatever else it says.
            return result is { Sent: > 0 } ? result : null;
        }
        catch (JsonException exception)
        {
            logger.LogWarning(
                exception,
                "Collector job {JobId} carried a result that is not an ICMP probe result.",
                job.Id);

            return null;
        }
    }
}
