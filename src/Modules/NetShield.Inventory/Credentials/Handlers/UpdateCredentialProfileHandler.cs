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
/// Replaces a profile's describable attributes. A PUT describes the profile as it should now be,
/// so an omitted optional member clears the stored value rather than leaving it alone.
/// </summary>
/// <remarks>
/// The material is untouched and unreadable from here — this handler takes no protector, so
/// there is no path through it to a plaintext credential. The kind is untouched too: it decides
/// what the sealed blob contains, and changing it would leave the material describing a protocol
/// the profile no longer claims to be for.
/// </remarks>
internal sealed class UpdateCredentialProfileHandler(
    InventoryDbContext context,
    OutboxEnlistment outbox,
    IResourceGuard guard,
    IAuditContext audit,
    IClock clock)
{
    public async Task<Result<CredentialProfileDetail>> HandleAsync(
        Guid id,
        UpdateCredentialProfileRequest request,
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

        // Checked against the kind the profile already has, not one the request asserts.
        Result attributes = CredentialKindRules.CheckAttributes(
            profile.Kind,
            request.Username,
            request.AuthAlgorithm,
            request.PrivacyAlgorithm);

        if (!attributes.IsSuccess)
        {
            return Result<CredentialProfileDetail>.Failure(attributes.Error);
        }

        // Changing the privacy algorithm to or from None changes which members the stored
        // material must carry, and the material is not being replaced here. Refusing is the only
        // answer that leaves the profile consistent: rotate the material to change it.
        if (profile.Kind is CredentialKind.SnmpV3
            && RequiresPrivacy(profile.PrivacyAlgorithm) != RequiresPrivacy(request.PrivacyAlgorithm))
        {
            return CredentialErrors.AttributesInvalid(
                profile.Kind,
                "cannot switch privacy on or off without new material. "
                + "Replace the material, which carries the privacy pass phrase, instead.");
        }

        string name = request.Name.Trim();
        string normalized = CredentialLimits.NormalizeName(name);

        if (!string.Equals(profile.NormalizedName, normalized, StringComparison.Ordinal)
            && await IsNameTakenAsync(normalized, id, cancellationToken))
        {
            return CredentialErrors.DuplicateName(name);
        }

        IReadOnlyDictionary<string, object?> before = profile.ToAuditSnapshot();

        profile.Name = name;
        profile.NormalizedName = normalized;
        profile.Description = CreateCredentialProfileHandler.Clean(request.Description);
        profile.Username = CreateCredentialProfileHandler.Clean(request.Username);
        profile.AuthAlgorithm = request.AuthAlgorithm;
        profile.PrivacyAlgorithm = request.PrivacyAlgorithm;
        profile.UpdatedAt = clock.UtcNow;

        outbox.Enlist(context, new CredentialProfileUpdated(profile.Id, profile.Kind, MaterialChanged: false));

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException failure) when (CreateCredentialProfileHandler.IsDuplicateName(failure))
        {
            return CredentialErrors.DuplicateName(name);
        }

        audit.Target(CredentialResolver.ResourceType, profile.Id.ToString());
        audit.Snapshot(before, profile.ToAuditSnapshot());

        IReadOnlyDictionary<Guid, int> counts = await GetCredentialProfileListHandler.CountDevicesAsync(
            [profile.Id],
            context,
            cancellationToken);

        return profile.ToDetail(counts.GetValueOrDefault(profile.Id));
    }

    private static bool RequiresPrivacy(SnmpPrivacyAlgorithm? algorithm) =>
        algorithm is not null and not SnmpPrivacyAlgorithm.None;

    private Task<bool> IsNameTakenAsync(
        string normalizedName,
        Guid excluding,
        CancellationToken cancellationToken) =>
        context.CredentialProfiles.AnyAsync(
            profile => profile.DeletedAt == null
                && profile.Id != excluding
                && profile.NormalizedName == normalizedName,
            cancellationToken);
}
