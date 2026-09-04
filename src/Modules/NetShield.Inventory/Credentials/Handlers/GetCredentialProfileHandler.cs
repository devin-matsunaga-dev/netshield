using Microsoft.EntityFrameworkCore;

using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory;

using NetShield.Inventory.Persistence;

using NetShield.Platform.Authorization;
using NetShield.Platform.Results;

namespace NetShield.Inventory.Credentials.Handlers;

/// <summary>
/// Everything the API will say about one credential profile — which is everything except the
/// material. Nothing here decrypts anything, and it takes no protector that could.
/// </summary>
internal sealed class GetCredentialProfileHandler(InventoryDbContext context, IResourceGuard guard)
{
    public async Task<Result<CredentialProfileDetail>> HandleAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        Result permitted = guard.Require(
            Permission.CredentialsManage,
            CredentialResolver.ResourceType,
            id.ToString());

        if (!permitted.IsSuccess)
        {
            return Result<CredentialProfileDetail>.Failure(permitted.Error);
        }

        CredentialProfile? profile = await context.CredentialProfiles.AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == id && candidate.DeletedAt == null,
                cancellationToken);

        if (profile is null)
        {
            return CredentialErrors.NotFound(id);
        }

        IReadOnlyDictionary<Guid, int> counts = await GetCredentialProfileListHandler.CountDevicesAsync(
            [profile.Id],
            context,
            cancellationToken);

        return profile.ToDetail(counts.GetValueOrDefault(profile.Id));
    }
}
