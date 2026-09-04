using Microsoft.EntityFrameworkCore;

using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory;
using NetShield.Contracts.Inventory.Events;

using NetShield.Inventory.Persistence;

using NetShield.Platform.Auditing;
using NetShield.Platform.Authorization;
using NetShield.Platform.Cryptography;
using NetShield.Platform.Messaging;
using NetShield.Platform.Results;
using NetShield.Platform.Time;

namespace NetShield.Inventory.Credentials.Handlers;

/// <summary>Creates a credential profile and seals its material.</summary>
/// <remarks>
/// The profile row and the <see cref="CredentialProfileCreated"/> outbox row are written by one
/// <c>SaveChangesAsync</c> on one context, so either both land or neither does
/// (ARCHITECTURE.md §5). The material is sealed before the row is built, so a failure to encrypt
/// is a failure to create rather than a row holding a placeholder.
/// </remarks>
internal sealed class CreateCredentialProfileHandler(
    InventoryDbContext context,
    CredentialMaterialProtector protector,
    OutboxEnlistment outbox,
    IResourceGuard guard,
    IAuditContext audit,
    IClock clock)
{
    public async Task<Result<CredentialProfileDetail>> HandleAsync(
        CreateCredentialProfileRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result permitted = guard.Require(Permission.CredentialsManage, CredentialResolver.ResourceType);

        if (!permitted.IsSuccess)
        {
            return Result<CredentialProfileDetail>.Failure(permitted.Error);
        }

        Result attributes = CredentialKindRules.CheckAttributes(
            request.Kind,
            request.Username,
            request.AuthAlgorithm,
            request.PrivacyAlgorithm);

        if (!attributes.IsSuccess)
        {
            return Result<CredentialProfileDetail>.Failure(attributes.Error);
        }

        CredentialMaterialPayload material = CredentialMaterialPayload.From(request.Material);

        Result complete = CredentialKindRules.CheckMaterial(request.Kind, request.PrivacyAlgorithm, material);

        if (!complete.IsSuccess)
        {
            return Result<CredentialProfileDetail>.Failure(complete.Error);
        }

        string name = request.Name.Trim();
        string normalized = CredentialLimits.NormalizeName(name);

        if (await IsNameTakenAsync(normalized, cancellationToken))
        {
            return CredentialErrors.DuplicateName(name);
        }

        DateTimeOffset now = clock.UtcNow;

        // The id first, because it is what the material is sealed against: the ciphertext is
        // bound to this profile and opens for no other.
        Guid id = Guid.CreateVersion7(now);
        EnvelopeCiphertext ciphertext = protector.Seal(id, material);

        CredentialProfile profile = new()
        {
            Id = id,
            Name = name,
            NormalizedName = normalized,
            Description = Clean(request.Description),
            Kind = request.Kind,
            Username = Clean(request.Username),
            AuthAlgorithm = request.AuthAlgorithm,
            PrivacyAlgorithm = request.PrivacyAlgorithm,
            KeyId = ciphertext.KeyId,
            WrappedDataKey = ciphertext.WrappedDataKey,
            MaterialCiphertext = ciphertext.Payload,
            MaterialUpdatedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

        context.CredentialProfiles.Add(profile);
        outbox.Enlist(context, new CredentialProfileCreated(profile.Id, profile.Kind));

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException failure) when (IsDuplicateName(failure))
        {
            // The check above narrows the window; the index closes it.
            return CredentialErrors.DuplicateName(name);
        }

        audit.Target(CredentialResolver.ResourceType, profile.Id.ToString());
        audit.Snapshot(before: null, after: profile.ToAuditSnapshot());

        return profile.ToDetail(deviceCount: 0);
    }

    private Task<bool> IsNameTakenAsync(string normalizedName, CancellationToken cancellationToken) =>
        context.CredentialProfiles.AnyAsync(
            profile => profile.DeletedAt == null && profile.NormalizedName == normalizedName,
            cancellationToken);

    /// <summary>
    /// Whether the database refused the write for the one reason this handler answers with a 409.
    /// Any other constraint failure is a bug and stays an exception.
    /// </summary>
    internal static bool IsDuplicateName(DbUpdateException failure) =>
        failure.InnerException is Npgsql.PostgresException
        {
            SqlState: Npgsql.PostgresErrorCodes.UniqueViolation
        } violation
        && violation.ConstraintName == CredentialProfileConfiguration.NameIndexName;

    /// <summary>An optional string that arrived as whitespace is absent, not blank.</summary>
    internal static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
