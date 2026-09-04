using Microsoft.EntityFrameworkCore;

using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory;
using NetShield.Contracts.Inventory.Events;

using NetShield.Inventory.Devices.Handlers;
using NetShield.Inventory.Persistence;

using NetShield.Platform.Auditing;
using NetShield.Platform.Authorization;
using NetShield.Platform.Messaging;
using NetShield.Platform.Results;
using NetShield.Platform.Time;

namespace NetShield.Inventory.Credentials.Handlers;

/// <summary>
/// Replaces the whole set of credential profiles assigned to a device.
/// </summary>
/// <remarks>
/// Whole-set replacement rather than an add and a remove, for the reason WP-1.1 chose PUT over
/// PATCH: the request says what is true afterwards, so two operators editing the same device
/// cannot interleave into a set neither of them asked for. The rows that were already right are
/// left where they are, so <c>created_at</c> still records when a credential was first given to
/// a device rather than when the set was last edited.
/// </remarks>
internal sealed class SetDeviceCredentialProfilesHandler(
    InventoryDbContext context,
    OutboxEnlistment outbox,
    IResourceGuard guard,
    IAuditContext audit,
    IClock clock)
{
    public async Task<Result<IReadOnlyList<CredentialProfileSummary>>> HandleAsync(
        Guid deviceId,
        SetDeviceCredentialProfilesRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result permitted = guard.Require(
            Permission.CredentialsManage,
            CredentialResolver.ResourceType,
            deviceId.ToString());

        if (!permitted.IsSuccess)
        {
            return Result<IReadOnlyList<CredentialProfileSummary>>.Failure(permitted.Error);
        }

        bool deviceExists = await context.Devices
            .AnyAsync(device => device.Id == deviceId && device.DeletedAt == null, cancellationToken);

        if (!deviceExists)
        {
            return Result<IReadOnlyList<CredentialProfileSummary>>.Failure(
                CredentialErrors.DeviceNotFound(deviceId));
        }

        // Sent twice is sent once. A caller repeating an id is describing the same assignment.
        IReadOnlyList<Guid> requested = [.. request.CredentialProfileIds.Distinct().Order()];

        if (requested.Count > CredentialLimits.MaximumAssignmentsPerDevice)
        {
            return Result<IReadOnlyList<CredentialProfileSummary>>.Failure(
                CredentialErrors.TooManyAssignments(CredentialLimits.MaximumAssignmentsPerDevice));
        }

        List<CredentialProfile> profiles = await context.CredentialProfiles
            .Where(profile => requested.Contains(profile.Id) && profile.DeletedAt == null)
            .ToListAsync(cancellationToken);

        // A profile that does not exist, or has been removed, is refused rather than skipped.
        // Silently dropping it would answer 200 to a request that did not happen.
        if (profiles.Count != requested.Count)
        {
            Guid missing = requested.First(id => profiles.TrueForAll(profile => profile.Id != id));

            return Result<IReadOnlyList<CredentialProfileSummary>>.Failure(CredentialErrors.NotFound(missing));
        }

        List<DeviceCredentialProfile> existing = await context.DeviceCredentialProfiles
            .Where(assignment => assignment.DeviceId == deviceId)
            .ToListAsync(cancellationToken);

        IReadOnlyList<Guid> before = [.. existing.Select(assignment => assignment.CredentialProfileId).Order()];

        if (before.SequenceEqual(requested))
        {
            // Nothing changed. No write, and no event — a subscriber that rebuilt a cache on
            // every PUT would rebuild it for every save of an unchanged form.
            return await SummariseAsync(profiles, cancellationToken);
        }

        DateTimeOffset now = clock.UtcNow;

        context.DeviceCredentialProfiles.RemoveRange(
            existing.Where(assignment => !requested.Contains(assignment.CredentialProfileId)));

        foreach (Guid profileId in requested.Where(
            id => existing.TrueForAll(assignment => assignment.CredentialProfileId != id)))
        {
            context.DeviceCredentialProfiles.Add(new DeviceCredentialProfile
            {
                Id = Guid.CreateVersion7(now),
                DeviceId = deviceId,
                CredentialProfileId = profileId,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        outbox.Enlist(context, new DeviceCredentialProfilesChanged(deviceId, requested));

        await context.SaveChangesAsync(cancellationToken);

        // The row is about the device: it is the device that changed, and a reader asking what
        // happened to this device is the one who needs to find it. The snapshot names profiles by
        // id rather than by name, because an audit row is read long after a profile may have been
        // renamed and the id is what still resolves.
        //
        // The key is profileIds and not credentialProfileIds. SecretRedactor blanks a property
        // whose name contains "credential" without stopping to consider that a list of uuids is
        // not a credential, so the honest-looking name would have stored [REDACTED] and said
        // nothing about what changed (the WP-0.5 lesson, again).
        audit.Target(GetDeviceListHandler.ResourceType, deviceId.ToString());
        audit.Snapshot(
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["profileIds"] = string.Join(", ", before)
            },
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["profileIds"] = string.Join(", ", requested)
            });

        return await SummariseAsync(profiles, cancellationToken);
    }

    private async Task<Result<IReadOnlyList<CredentialProfileSummary>>> SummariseAsync(
        List<CredentialProfile> profiles,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<Guid, int> counts = await GetCredentialProfileListHandler.CountDevicesAsync(
            [.. profiles.Select(profile => profile.Id)],
            context,
            cancellationToken);

        return Result<IReadOnlyList<CredentialProfileSummary>>.Success(
            [.. profiles
                .OrderBy(profile => profile.Name, StringComparer.Ordinal)
                .Select(profile => profile.ToSummary(counts.GetValueOrDefault(profile.Id)))]);
    }
}
