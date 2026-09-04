using Microsoft.EntityFrameworkCore;

using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory;
using NetShield.Contracts.Inventory.Events;

using NetShield.Inventory.Persistence;

using NetShield.Platform.Auditing;
using NetShield.Platform.Authorization;
using NetShield.Platform.Messaging;
using NetShield.Platform.Results;
using NetShield.Platform.Time;

namespace NetShield.Inventory.Credentials.Handlers;

/// <summary>
/// Replaces a profile's material — the rotation an operator performs when a password changes or
/// a credential is believed to have leaked.
/// </summary>
/// <remarks>
/// <para>
/// Its own endpoint rather than a member on the update request, because the material can never be
/// read back: a whole-resource PUT that carried it would have nothing to send on the round trip,
/// and the obvious reading of an absent value — leave it alone — is the opposite of what
/// whole-resource replacement means everywhere else in this API.
/// </para>
/// <para>
/// It is sealed under the ring's <em>active</em> key, whatever the profile was on before. A
/// rotation of the credential is therefore also a rotation of its wrapping, for free.
/// </para>
/// </remarks>
internal sealed class ReplaceCredentialMaterialHandler(
    InventoryDbContext context,
    CredentialMaterialProtector protector,
    OutboxEnlistment outbox,
    IResourceGuard guard,
    IAuditContext audit,
    IClock clock)
{
    public async Task<Result<CredentialProfileDetail>> HandleAsync(
        Guid id,
        ReplaceCredentialMaterialRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result permitted = guard.Require(
            Permission.CredentialsManage,
            CredentialResolver.ResourceType,
            id.ToString());

        if (!permitted.IsSuccess)
        {
            return Result<CredentialProfileDetail>.Failure(permitted.Error);
        }

        CredentialProfile? profile = await context.CredentialProfiles
            .SingleOrDefaultAsync(
                candidate => candidate.Id == id && candidate.DeletedAt == null,
                cancellationToken);

        if (profile is null)
        {
            return CredentialErrors.NotFound(id);
        }

        CredentialMaterialPayload material = CredentialMaterialPayload.From(request.Material);

        Result complete = CredentialKindRules.CheckMaterial(profile.Kind, profile.PrivacyAlgorithm, material);

        if (!complete.IsSuccess)
        {
            return Result<CredentialProfileDetail>.Failure(complete.Error);
        }

        // Taken before anything is written: the row's "before" has to be the profile as it was,
        // and materialUpdatedAt is about to stop being that.
        IReadOnlyDictionary<string, object?> before = profile.ToAuditSnapshot();

        DateTimeOffset now = clock.UtcNow;

        profile.SetCiphertext(protector.Seal(profile.Id, material));
        profile.MaterialUpdatedAt = now;
        profile.UpdatedAt = now;

        outbox.Enlist(context, new CredentialProfileUpdated(profile.Id, profile.Kind, MaterialChanged: true));

        await context.SaveChangesAsync(cancellationToken);

        // The audit row records that the material changed and when, and nothing about what it
        // changed to or from. The two snapshots differ in exactly one member, materialUpdatedAt,
        // which is the whole of what there is to say about a secret (SPEC.md §5).
        audit.Target(CredentialResolver.ResourceType, profile.Id.ToString());
        audit.Snapshot(before, profile.ToAuditSnapshot());

        IReadOnlyDictionary<Guid, int> counts = await GetCredentialProfileListHandler.CountDevicesAsync(
            [profile.Id],
            context,
            cancellationToken);

        return profile.ToDetail(counts.GetValueOrDefault(profile.Id));
    }
}
