using Microsoft.EntityFrameworkCore;

using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory;

using NetShield.Inventory.Persistence;

using NetShield.Platform.Authorization;
using NetShield.Platform.Results;

namespace NetShield.Inventory.Credentials.Handlers;

/// <summary>
/// The credential profiles a device may be reached with, as summaries — names and kinds, never
/// material.
/// </summary>
/// <remarks>
/// Not paginated, and deliberately so: the set is bounded by
/// <see cref="CredentialLimits.MaximumAssignmentsPerDevice"/> at the write, so this endpoint
/// cannot return an unbounded collection and CONVENTIONS.md §4's rule is satisfied by the bound
/// rather than by a cursor nobody would page.
/// </remarks>
internal sealed class GetDeviceCredentialProfilesHandler(InventoryDbContext context, IResourceGuard guard)
{
    public async Task<Result<IReadOnlyList<CredentialProfileSummary>>> HandleAsync(
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        Result permitted = guard.Require(
            Permission.CredentialsManage,
            CredentialResolver.ResourceType,
            deviceId.ToString());

        if (!permitted.IsSuccess)
        {
            return Result<IReadOnlyList<CredentialProfileSummary>>.Failure(permitted.Error);
        }

        bool deviceExists = await context.Devices.AsNoTracking()
            .AnyAsync(device => device.Id == deviceId && device.DeletedAt == null, cancellationToken);

        if (!deviceExists)
        {
            return Result<IReadOnlyList<CredentialProfileSummary>>.Failure(
                CredentialErrors.DeviceNotFound(deviceId));
        }

        List<CredentialProfile> profiles = await context.DeviceCredentialProfiles.AsNoTracking()
            .Where(assignment => assignment.DeviceId == deviceId)
            .Join(
                context.CredentialProfiles.AsNoTracking().Where(profile => profile.DeletedAt == null),
                assignment => assignment.CredentialProfileId,
                profile => profile.Id,
                (_, profile) => profile)
            .OrderBy(profile => profile.Name)
            .ToListAsync(cancellationToken);

        IReadOnlyDictionary<Guid, int> counts = await GetCredentialProfileListHandler.CountDevicesAsync(
            [.. profiles.Select(profile => profile.Id)],
            context,
            cancellationToken);

        return Result<IReadOnlyList<CredentialProfileSummary>>.Success(
            [.. profiles.Select(profile => profile.ToSummary(counts.GetValueOrDefault(profile.Id)))]);
    }
}
