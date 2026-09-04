using Microsoft.EntityFrameworkCore;

using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory.Events;

using NetShield.Inventory.Persistence;

using NetShield.Platform.Auditing;
using NetShield.Platform.Authorization;
using NetShield.Platform.Messaging;
using NetShield.Platform.Results;
using NetShield.Platform.Time;

namespace NetShield.Inventory.Credentials.Handlers;

/// <summary>
/// Removes a credential profile. Soft delete (CONVENTIONS.md §3): the row stays so that the audit
/// rows naming this profile still resolve, and it stops holding its name.
/// </summary>
/// <remarks>
/// <para>
/// Every assignment of the profile is deleted outright, and that one is a hard delete. A soft
/// delete of the profile is a statement about history; an assignment that survived it would be a
/// statement about the present, and a later query that forgot to filter on the profile's
/// <c>deleted_at</c> would hand a collector a credential an operator believes they revoked.
/// </para>
/// <para>
/// The sealed material stays on the row. Erasing it would make the removal irreversible in a way
/// nothing here promises, and would leave a soft-deleted row that cannot satisfy its own NOT NULL
/// columns. What revokes the credential is that no live profile carries it and no assignment
/// points at it; if a package ever needs the bytes actually gone, that is a shredding policy with
/// its own retention question, not a line in this handler.
/// </para>
/// </remarks>
internal sealed class DeleteCredentialProfileHandler(
    InventoryDbContext context,
    OutboxEnlistment outbox,
    IResourceGuard guard,
    IAuditContext audit,
    IClock clock)
{
    public async Task<Result> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        Result permitted = guard.Require(
            Permission.CredentialsManage,
            CredentialResolver.ResourceType,
            id.ToString());

        if (!permitted.IsSuccess)
        {
            return permitted;
        }

        CredentialProfile? profile = await context.CredentialProfiles
            .SingleOrDefaultAsync(
                candidate => candidate.Id == id && candidate.DeletedAt == null,
                cancellationToken);

        if (profile is null)
        {
            // Deleting a profile that is already deleted is 404 rather than 204. A caller who
            // believes they revoked something they did not is worse served by silence.
            return CredentialErrors.NotFound(id);
        }

        IReadOnlyDictionary<string, object?> before = profile.ToAuditSnapshot();

        DateTimeOffset now = clock.UtcNow;

        profile.DeletedAt = now;
        profile.UpdatedAt = now;

        List<DeviceCredentialProfile> assignments = await context.DeviceCredentialProfiles
            .Where(assignment => assignment.CredentialProfileId == id)
            .ToListAsync(cancellationToken);

        context.DeviceCredentialProfiles.RemoveRange(assignments);

        outbox.Enlist(context, new CredentialProfileRemoved(profile.Id, profile.Kind));

        // Every device that just lost a credential is told so, so that scheduling does not go on
        // queuing work against a credential nothing will resolve.
        foreach (Guid deviceId in assignments.Select(assignment => assignment.DeviceId).Distinct().Order())
        {
            outbox.Enlist(
                context,
                new DeviceCredentialProfilesChanged(
                    deviceId,
                    [.. await RemainingForDeviceAsync(deviceId, id, cancellationToken)]));
        }

        await context.SaveChangesAsync(cancellationToken);

        audit.Target(CredentialResolver.ResourceType, profile.Id.ToString());
        audit.Snapshot(before, after: null);

        return Result.Success;
    }

    /// <summary>
    /// What the device is left assigned once this profile is gone. Read before the save, so it
    /// excludes the profile being removed explicitly rather than relying on the delete having
    /// already happened.
    /// </summary>
    private Task<List<Guid>> RemainingForDeviceAsync(
        Guid deviceId,
        Guid removing,
        CancellationToken cancellationToken) =>
        context.DeviceCredentialProfiles.AsNoTracking()
            .Where(assignment => assignment.DeviceId == deviceId && assignment.CredentialProfileId != removing)
            .Select(assignment => assignment.CredentialProfileId)
            .OrderBy(profileId => profileId)
            .ToListAsync(cancellationToken);
}
