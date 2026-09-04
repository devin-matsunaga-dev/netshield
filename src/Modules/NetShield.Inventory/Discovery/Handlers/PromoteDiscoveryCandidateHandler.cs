using System.Net;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory;
using NetShield.Contracts.Inventory.Events;

using NetShield.Inventory.Devices;
using NetShield.Inventory.Devices.Handlers;
using NetShield.Inventory.Persistence;

using NetShield.Platform.Auditing;
using NetShield.Platform.Authorization;
using NetShield.Platform.Messaging;
using NetShield.Platform.Results;
using NetShield.Platform.Time;

namespace NetShield.Inventory.Discovery.Handlers;

/// <summary>
/// Turns a candidate into a device: the review step SPEC.md §2 asks for, taken.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is the only way a sweep becomes inventory.</strong> Nothing in the result path
/// creates a device, which is the WP-1.6 criterion that results appear as reviewable candidates
/// rather than auto-created devices; this is where a person says yes.
/// </para>
/// <para>
/// The device is created exactly as <c>POST /api/v1/devices</c> would create it, at the
/// candidate's address, with <c>Unknown</c> for the vendor and for the state. A sweep
/// established neither: it sent an echo request and something answered. What the host is comes
/// from the SNMP walk WP-1.5 built, which can be asked for as soon as the device has a
/// credential — and until it is, the device says it does not know rather than guessing.
/// </para>
/// <para>
/// It assigns no credential profile. WP-1.2 put credential assignment behind
/// <c>CredentialsManage</c>, and promotion is <see cref="Permission.InventoryWrite"/>; a
/// promotion that assigned one would be a way around that boundary rather than a convenience.
/// </para>
/// </remarks>
internal sealed class PromoteDiscoveryCandidateHandler(
    InventoryDbContext context,
    OutboxEnlistment outbox,
    IResourceGuard guard,
    IAuditContext audit,
    IClock clock,
    ILogger<PromoteDiscoveryCandidateHandler> logger)
{
    public async Task<Result<DeviceDetail>> HandleAsync(
        Guid candidateId,
        PromoteDiscoveryCandidateRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result permitted = guard.Require(
            Permission.InventoryWrite,
            GetDiscoveryCandidateListHandler.ResourceType,
            candidateId.ToString());

        if (!permitted.IsSuccess)
        {
            return Result<DeviceDetail>.Failure(permitted.Error);
        }

        DiscoveryCandidate? candidate = await context.DiscoveryCandidates
            .SingleOrDefaultAsync(row => row.Id == candidateId, cancellationToken);

        if (candidate is null)
        {
            return DiscoveryErrors.CandidateNotFound(candidateId);
        }

        if (candidate.Status != DiscoveryCandidateStatus.New)
        {
            return DiscoveryErrors.CandidateSettled(candidateId);
        }

        IPAddress address = candidate.Address;

        if (await IsAddressTakenAsync(address, cancellationToken))
        {
            return DeviceErrors.DuplicateAddress(address.ToString());
        }

        DateTimeOffset now = clock.UtcNow;

        Device device = new()
        {
            Id = Guid.CreateVersion7(now),
            Hostname = request.Hostname.Trim(),
            PrimaryIpAddress = address,

            // Nothing has identified this host. A sweep asked whether anything was there.
            Vendor = DeviceVendor.Unknown,
            Site = CreateDeviceHandler.Clean(request.Site),
            Role = request.Role,
            Criticality = request.Criticality,
            Environment = request.Environment,
            Owner = CreateDeviceHandler.Clean(request.Owner),
            Tags = DeviceTags.Normalize(request.Tags),
            Notes = CreateDeviceHandler.Clean(request.Notes),

            // Reachability is observed, never asserted — and answering one echo request is not
            // the two consecutive observations WP-1.4 wants before it calls a device online.
            State = DeviceState.Unknown,
            CreatedAt = now,
            UpdatedAt = now
        };

        context.Devices.Add(device);

        candidate.Status = DiscoveryCandidateStatus.Promoted;
        candidate.PromotedDeviceId = device.Id;
        candidate.SettledAt = now;
        candidate.UpdatedAt = now;

        outbox.Enlist(
            context,
            new DeviceCreated(device.Id, device.Hostname, device.PrimaryIpAddress.ToString()));

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException failure) when (CreateDeviceHandler.IsDuplicateAddress(failure))
        {
            // The check above narrows the window; the index closes it. Two operators promoting
            // the same candidate, or promoting a candidate while somebody creates a device by
            // hand at the same address, both land here.
            return DeviceErrors.DuplicateAddress(address.ToString());
        }

        logger.LogInformation(
            "Discovery candidate {CandidateId} was promoted to device {DeviceId}",
            candidateId,
            device.Id);

        audit.Target(GetDiscoveryCandidateListHandler.ResourceType, candidateId.ToString());
        audit.Snapshot(before: null, after: device.ToAuditSnapshot());

        return device.ToDetail();
    }

    private Task<bool> IsAddressTakenAsync(IPAddress address, CancellationToken cancellationToken) =>
        context.Devices.AnyAsync(
            device => device.DeletedAt == null && device.PrimaryIpAddress.Equals(address),
            cancellationToken);
}
