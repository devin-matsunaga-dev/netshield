using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NetShield.Inventory.Devices;
using NetShield.Inventory.Persistence;

using NetShield.Platform.Results;

namespace NetShield.Inventory.Credentials;

/// <inheritdoc cref="ICredentialResolver"/>
internal sealed class CredentialResolver(
    InventoryDbContext context,
    CredentialMaterialProtector protector,
    ILogger<CredentialResolver> logger) : ICredentialResolver
{
    /// <summary>What an audit row and a refusal call this kind of thing.</summary>
    internal const string ResourceType = "credential-profile";

    public async Task<Result<ResolvedCredential>> ResolveAsync(
        Guid credentialProfileId,
        CancellationToken cancellationToken)
    {
        CredentialProfile? profile = await context.CredentialProfiles.AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == credentialProfileId && candidate.DeletedAt == null,
                cancellationToken);

        if (profile is null)
        {
            return CredentialErrors.NotFound(credentialProfileId);
        }

        // Information, because a credential being opened is a business event and one an
        // investigation reads back. The property is named ProfileId rather than
        // CredentialProfileId on purpose: SecretRedactor blanks a property whose name contains
        // "credential", so the honest-looking name would have written [REDACTED] and told an
        // investigator nothing about which profile was used.
        logger.LogInformation(
            "Opened credential profile {ProfileId} of kind {Kind}.",
            profile.Id,
            profile.Kind);

        return new ResolvedCredential(
            profile.Id,
            profile.Kind,
            profile.Username,
            profile.AuthAlgorithm,
            profile.PrivacyAlgorithm,
            protector.Open(profile));
    }

    public async Task<Result<IReadOnlyList<CredentialAssignment>>> ForDeviceAsync(
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        bool deviceExists = await context.Devices
            .AnyAsync(device => device.Id == deviceId && device.DeletedAt == null, cancellationToken);

        if (!deviceExists)
        {
            return Result<IReadOnlyList<CredentialAssignment>>.Failure(CredentialErrors.DeviceNotFound(deviceId));
        }

        List<CredentialAssignment> assignments = await context.DeviceCredentialProfiles.AsNoTracking()
            .Where(assignment => assignment.DeviceId == deviceId)
            .Join(
                context.CredentialProfiles.AsNoTracking().Where(profile => profile.DeletedAt == null),
                assignment => assignment.CredentialProfileId,
                profile => profile.Id,
                (_, profile) => new CredentialAssignment(profile.Id, profile.Kind, profile.Name))
            .OrderBy(assignment => assignment.Name)
            .ToListAsync(cancellationToken);

        return assignments;
    }
}
