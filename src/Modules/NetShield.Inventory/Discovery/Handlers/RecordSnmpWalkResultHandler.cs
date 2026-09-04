using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NetShield.Contracts.Collector;
using NetShield.Contracts.Collector.Events;
using NetShield.Contracts.Inventory;
using NetShield.Contracts.Inventory.Events;

using NetShield.Inventory.Collector;
using NetShield.Inventory.Devices;
using NetShield.Inventory.Persistence;

using NetShield.Platform.Messaging;
using NetShield.Platform.Time;

namespace NetShield.Inventory.Discovery.Handlers;

/// <summary>
/// Reads a finished SNMP walk and records what the device turned out to be.
/// </summary>
/// <remarks>
/// <para>
/// The second subscriber to <c>CollectorJobCompleted</c>, beside WP-1.4's reachability handler.
/// It reads only the rows this package queued — a <c>Discover</c> whose parameters name the SNMP
/// walk — and leaves every other job to whoever queued it, because WP-1.6's range sweep will be
/// a <c>Discover</c> sitting in the same table looking identical from the outside.
/// </para>
/// <para>
/// <strong>A failed walk changes nothing but the record of the failure.</strong> An unreachable
/// device, a refused community string or a timeout means nothing was established, and replacing
/// a known fingerprint with an empty one because a single walk did not get through would lose
/// what an earlier walk found. The same reasoning WP-1.4 applied to device state, applied to
/// device identity.
/// </para>
/// <para>
/// <strong>Safe to run twice.</strong> Outbox delivery is at-least-once. The fingerprint row
/// records the job it last applied and a redelivery is dropped — which matters more here than for
/// a counter, because a second application would re-run the override comparison against a
/// baseline the first application had already moved.
/// </para>
/// <para>
/// <strong>Discovered against overridden.</strong> A walk owns the four identity facts unless an
/// operator has changed one away from what the previous walk discovered, in which case that field
/// is left alone and named in <c>device_fingerprints.overridden_fields</c>. A device that has
/// never been walked has no such baseline, so the first walk owns everything — which is the point
/// of fingerprinting: correcting what somebody guessed when they typed the device in.
/// </para>
/// </remarks>
internal sealed class RecordSnmpWalkResultHandler(
    InventoryDbContext context,
    OutboxEnlistment outbox,
    IClock clock,
    ILogger<RecordSnmpWalkResultHandler> logger) : IIntegrationEventHandler<CollectorJobCompleted>
{
    /// <summary>The member names <c>overridden_fields</c> uses, spelled as the API spells them.</summary>
    private const string VendorField = "vendor";
    private const string ModelField = "model";
    private const string OsVersionField = "osVersion";
    private const string SerialNumberField = "serialNumber";

    public async Task HandleAsync(
        CollectorJobCompleted integrationEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        if (integrationEvent.Kind != CollectorJobKind.Discover
            || integrationEvent.DeviceId is not { } deviceId)
        {
            return;
        }

        CollectorJob? job = await context.CollectorJobs.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == integrationEvent.JobId, cancellationToken);

        if (job is null || !IsSnmpWalk(job))
        {
            return;
        }

        DateTimeOffset now = clock.UtcNow;

        DeviceFingerprint? fingerprint = await context.DeviceFingerprints
            .SingleOrDefaultAsync(row => row.DeviceId == deviceId, cancellationToken);

        bool firstWalk = fingerprint is null;

        fingerprint ??= Create(deviceId, now);

        if (fingerprint.LastAppliedJobId == integrationEvent.JobId)
        {
            return;
        }

        fingerprint.LastAppliedJobId = integrationEvent.JobId;
        fingerprint.UpdatedAt = now;

        if (integrationEvent.Outcome != CollectorJobOutcome.Succeeded)
        {
            // The detail has already been through SecretRedactor on its way into the column
            // (WP-1.3), so it is safe to carry across and safe to log.
            fingerprint.LastError = job.Detail ?? "The collector could not walk the device.";

            logger.LogWarning(
                "An SNMP walk of device {DeviceId} could not be performed: {Detail}",
                deviceId,
                fingerprint.LastError);
        }
        else if (Parse(job) is { } result)
        {
            await ApplyAsync(fingerprint, result, deviceId, firstWalk, now, cancellationToken);
        }
        else
        {
            // A successful job whose payload is not a walk result is a collector reporting
            // something this package cannot read. It is the collector's problem, recorded the
            // same way, and specifically not evidence that the device has no interfaces.
            fingerprint.LastError = "The collector reported a result this walk could not read.";

            logger.LogWarning(
                "Collector job {JobId} succeeded but carried no readable SNMP walk result.",
                integrationEvent.JobId);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Folds a walk that ran into the device, its fingerprint and its interfaces.</summary>
    private async Task ApplyAsync(
        DeviceFingerprint fingerprint,
        SnmpWalkResult result,
        Guid deviceId,
        bool firstWalk,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        Device? device = await context.Devices.SingleOrDefaultAsync(
            candidate => candidate.Id == deviceId && candidate.DeletedAt == null,
            cancellationToken);

        if (device is null)
        {
            // Removed while the walk was in flight. There is nothing left to fingerprint, and
            // writing interface rows for a device an operator deleted would resurrect it in
            // every join that forgot to filter.
            logger.LogInformation(
                "An SNMP walk result for device {DeviceId} was dropped: the device has been removed.",
                deviceId);

            return;
        }

        fingerprint.LastError = null;
        fingerprint.LastWalkAt = now;

        DeviceVendor previousDiscoveredVendor = fingerprint.Vendor;
        string? previousDiscoveredModel = fingerprint.Model;
        string? previousDiscoveredOsVersion = fingerprint.OsVersion;
        string? previousDiscoveredSerial = fingerprint.SerialNumber;

        // What the walk saw. Recorded whether or not the device row is allowed to take it, so
        // that "discovered X, operator says Y" is answerable from the row.
        DeviceVendor? walkedVendor = ParseVendor(result, fingerprint, deviceId);

        fingerprint.Vendor = walkedVendor ?? previousDiscoveredVendor;
        fingerprint.ReducedCapability = result.ReducedCapability;
        fingerprint.SysObjectId = Text(result.SysObjectId, DiscoveryLimits.ObjectIdLength);
        fingerprint.SysDescr = Text(result.SysDescr, DiscoveryLimits.DescriptionLength);
        fingerprint.SysName = Text(result.SysName, DiscoveryLimits.NameLength);
        fingerprint.SysContact = Text(result.SysContact, DiscoveryLimits.NameLength);
        fingerprint.SysLocation = Text(result.SysLocation, DiscoveryLimits.NameLength);
        fingerprint.UptimeSeconds = result.UptimeSeconds;
        fingerprint.InterfaceCount = result.InterfaceCount;
        fingerprint.InterfacesTruncated = result.InterfacesTruncated;

        // A fact the walk did not establish leaves the previous discovered value standing. A
        // device that answered no serial this time has not stopped having one, and letting the
        // absence through would make the next walk read as an operator override.
        string? walkedModel = Text(result.Model, DiscoveryLimits.NameLength);
        string? walkedOsVersion = Text(result.OsVersion, DiscoveryLimits.NameLength);
        string? walkedSerial = Text(result.SerialNumber, DiscoveryLimits.NameLength);

        fingerprint.Model = walkedModel ?? previousDiscoveredModel;
        fingerprint.OsVersion = walkedOsVersion ?? previousDiscoveredOsVersion;
        fingerprint.SerialNumber = walkedSerial ?? previousDiscoveredSerial;

        List<string> overridden = [];
        bool identityChanged = false;

        if (walkedVendor is { } vendor)
        {
            if (!firstWalk && device.Vendor != previousDiscoveredVendor)
            {
                overridden.Add(VendorField);
            }
            else if (device.Vendor != vendor)
            {
                device.Vendor = vendor;
                identityChanged = true;
            }
        }

        identityChanged |= Adopt(
            ModelField,
            walkedModel,
            previousDiscoveredModel,
            device.Model,
            firstWalk,
            overridden,
            value => device.Model = value);

        identityChanged |= Adopt(
            OsVersionField,
            walkedOsVersion,
            previousDiscoveredOsVersion,
            device.OsVersion,
            firstWalk,
            overridden,
            value => device.OsVersion = value);

        identityChanged |= Adopt(
            SerialNumberField,
            walkedSerial,
            previousDiscoveredSerial,
            device.SerialNumber,
            firstWalk,
            overridden,
            value => device.SerialNumber = value);

        fingerprint.OverriddenFields = overridden;

        if (identityChanged)
        {
            // Unlike a reachability transition, which deliberately does not stamp the device
            // (WP-1.4): what a device *is* has changed, and that is exactly the kind of change
            // the device list's updated_at column is for. Stamped only when a stored value
            // actually moved, so re-walking an unchanged switch does not look like an edit.
            device.UpdatedAt = now;
        }

        bool interfacesChanged = await ReconcileInterfacesAsync(
            deviceId,
            result,
            now,
            cancellationToken);

        if (overridden.Count > 0)
        {
            logger.LogInformation(
                "An SNMP walk of device {DeviceId} left {Fields} as the operator set them.",
                deviceId,
                string.Join(", ", overridden));
        }

        if (!identityChanged && !interfacesChanged)
        {
            // The device is exactly as it was recorded. Publishing anyway would have every
            // subscriber rebuild whatever it caches each time somebody re-walks a switch.
            return;
        }

        // The event and every row above commit together, so nothing can subscribe to a
        // fingerprint that was rolled back (ARCHITECTURE.md §5).
        outbox.Enlist(
            context,
            new DeviceFingerprinted(
                deviceId,
                device.Hostname,
                device.Vendor,
                result.ReducedCapability,
                device.Model,
                device.OsVersion,
                device.SerialNumber,
                result.InterfaceCount,
                now));
    }

    /// <summary>
    /// Writes one discovered fact onto the device, unless an operator has claimed that field.
    /// </summary>
    /// <returns>Whether the device's value actually changed.</returns>
    private static bool Adopt(
        string field,
        string? walked,
        string? previousDiscovered,
        string? current,
        bool firstWalk,
        List<string> overridden,
        Action<string?> assign)
    {
        if (walked is null)
        {
            return false;
        }

        if (!firstWalk && !string.Equals(current, previousDiscovered, StringComparison.Ordinal))
        {
            overridden.Add(field);

            return false;
        }

        if (string.Equals(current, walked, StringComparison.Ordinal))
        {
            return false;
        }

        assign(walked);

        return true;
    }

    /// <summary>
    /// Brings <c>device_interfaces</c> into line with what the walk saw.
    /// </summary>
    /// <returns>Whether an interface was added or removed. A status change is not one.</returns>
    /// <remarks>
    /// Rows are removed only when the walk read the whole table. A walk that hit the interface
    /// ceiling saw part of it, so an interface it did not mention may simply not have been
    /// reached — deleting on that evidence would empty the inventory of the largest devices in
    /// the estate.
    /// </remarks>
    private async Task<bool> ReconcileInterfacesAsync(
        Guid deviceId,
        SnmpWalkResult result,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<SnmpWalkInterface> observed = result.Interfaces ?? [];

        List<DeviceInterface> existing = await context.DeviceInterfaces
            .Where(row => row.DeviceId == deviceId)
            .ToListAsync(cancellationToken);

        Dictionary<int, DeviceInterface> byIndex = existing.ToDictionary(row => row.IfIndex);
        HashSet<int> seen = [];
        bool changed = false;

        foreach (SnmpWalkInterface item in observed)
        {
            // A payload naming one ifIndex twice would otherwise violate the unique index and
            // fail the whole walk. Keeping the first is arbitrary; failing on it is worse.
            if (!seen.Add(item.Index))
            {
                continue;
            }

            if (byIndex.TryGetValue(item.Index, out DeviceInterface? row))
            {
                Update(row, item, now);
            }
            else
            {
                context.DeviceInterfaces.Add(New(deviceId, item, now));
                changed = true;
            }
        }

        if (result.InterfacesTruncated)
        {
            return changed;
        }

        List<DeviceInterface> gone = existing.Where(row => !seen.Contains(row.IfIndex)).ToList();

        if (gone.Count > 0)
        {
            context.DeviceInterfaces.RemoveRange(gone);
            changed = true;

            logger.LogInformation(
                "An SNMP walk of device {DeviceId} removed {Count} interfaces it no longer reports.",
                deviceId,
                gone.Count);
        }

        return changed;
    }

    private static DeviceInterface New(Guid deviceId, SnmpWalkInterface item, DateTimeOffset now)
    {
        DeviceInterface row = new()
        {
            Id = Guid.CreateVersion7(now),
            DeviceId = deviceId,
            IfIndex = item.Index,
            FirstSeenAt = now,
            CreatedAt = now
        };

        Update(row, item, now);

        return row;
    }

    private static void Update(DeviceInterface row, SnmpWalkInterface item, DateTimeOffset now)
    {
        row.Name = Text(item.Name, DiscoveryLimits.InterfaceTextLength);
        row.Description = Text(item.Description, DiscoveryLimits.InterfaceTextLength);
        row.Alias = Text(item.Alias, DiscoveryLimits.InterfaceTextLength);
        row.InterfaceType = item.InterfaceType;
        row.Mtu = item.Mtu;
        row.SpeedBitsPerSecond = item.SpeedBitsPerSecond;
        row.PhysicalAddress = Text(item.PhysicalAddress, DiscoveryLimits.PhysicalAddressLength);
        row.AdminStatus = item.AdminStatus;
        row.OperStatus = item.OperStatus;
        row.LastSeenAt = now;
        row.UpdatedAt = now;
    }

    private DeviceFingerprint Create(Guid deviceId, DateTimeOffset now)
    {
        DeviceFingerprint row = new()
        {
            Id = Guid.CreateVersion7(now),
            DeviceId = deviceId,
            Vendor = DeviceVendor.Unknown,
            CreatedAt = now,
            UpdatedAt = now
        };

        context.DeviceFingerprints.Add(row);

        return row;
    }

    /// <summary>
    /// The <see cref="DeviceVendor"/> the collector named, or nothing if this build has no such
    /// member.
    /// </summary>
    /// <remarks>
    /// The two vendor lists are matched by string across two repositories with no generator
    /// between them. A collector newer than the API can name a platform this build does not have,
    /// and the honest answer is to leave the device's vendor alone and say so on the row —
    /// recording it as generic SNMP would claim the device was unrecognised when it was in fact
    /// recognised by something this deployment has not caught up with.
    /// </remarks>
    private DeviceVendor? ParseVendor(SnmpWalkResult result, DeviceFingerprint fingerprint, Guid deviceId)
    {
        if (Enum.TryParse(result.Vendor, ignoreCase: false, out DeviceVendor vendor)
            && Enum.IsDefined(vendor))
        {
            return vendor;
        }

        fingerprint.LastError =
            $"The collector resolved a vendor this build does not have a member for: '{result.Vendor}'.";

        logger.LogWarning(
            "An SNMP walk of device {DeviceId} named vendor {Vendor}, which this build does not know.",
            deviceId,
            result.Vendor);

        return null;
    }

    /// <summary>Whether this job is one this package queued.</summary>
    private static bool IsSnmpWalk(CollectorJob job)
    {
        if (string.IsNullOrEmpty(job.Parameters))
        {
            return false;
        }

        try
        {
            SnmpWalkParameters? parameters = JsonSerializer.Deserialize(
                job.Parameters,
                DiscoverySerializerContext.Default.SnmpWalkParameters);

            return string.Equals(parameters?.Walk, SnmpWalkParameters.WalkName, StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            // Another package's parameter document, shaped differently. Not ours.
            return false;
        }
    }

    /// <summary>The walk result on the job row, or nothing if it does not read as one.</summary>
    private SnmpWalkResult? Parse(CollectorJob job)
    {
        if (string.IsNullOrEmpty(job.Result))
        {
            return null;
        }

        try
        {
            SnmpWalkResult? result = JsonSerializer.Deserialize(
                job.Result,
                DiscoverySerializerContext.Default.SnmpWalkResult);

            // The discriminator has to agree with the parameters. A payload that does not name
            // this walk is a collector answering a question nobody asked.
            return string.Equals(result?.Walk, SnmpWalkParameters.WalkName, StringComparison.Ordinal)
                ? result
                : null;
        }
        catch (JsonException exception)
        {
            logger.LogWarning(
                exception,
                "Collector job {JobId} carried a result that is not an SNMP walk result.",
                job.Id);

            return null;
        }
    }

    /// <summary>
    /// A device-supplied string, trimmed to the column and reduced to nothing when it is blank.
    /// </summary>
    private static string? Text(string? value, int limit)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim();

        return trimmed.Length <= limit ? trimmed : trimmed[..limit];
    }
}
