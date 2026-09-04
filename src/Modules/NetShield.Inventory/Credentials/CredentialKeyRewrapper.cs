using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NetShield.Inventory.Persistence;

using NetShield.Platform.Auditing;
using NetShield.Platform.Cryptography;
using NetShield.Platform.Logging;
using NetShield.Platform.Time;

namespace NetShield.Inventory.Credentials;

/// <summary>
/// Moves every stored credential onto the ring's active key-encryption key — the rotation path
/// WP-1.2 asks for.
/// </summary>
/// <remarks>
/// <para>
/// Nothing is decrypted. Each row's data-encryption key is unwrapped under the key it was wrapped
/// with and re-wrapped under the active one; the material ciphertext is carried across untouched.
/// So a rotation costs one small update per profile, never reconstructs a plaintext credential,
/// and can run while the API is serving — a request that reads a row this has not reached yet
/// opens it with the old key, which is still in the ring, and one that reads a row it has already
/// moved opens it with the new one.
/// </para>
/// <para>
/// Removed profiles are re-wrapped too. A retired key can only be deleted once no row anywhere
/// depends on it, and a soft-deleted row is still a row.
/// </para>
/// <para>
/// It is exposed as a command rather than as an endpoint. Key rotation is key management, not
/// application traffic: a long-lived HTTP route would put the most privileged cryptographic
/// operation in the system permanently on the web attack surface, and the only thing that would
/// buy is the audit row the middleware writes for free — which this writes itself, below. If an
/// administration screen ever needs to start a rotation, the HTTP orchestration for that is a
/// package that says so.
/// </para>
/// </remarks>
public sealed class CredentialKeyRewrapper(
    InventoryDbContext context,
    IEnvelopeEncryptor encryptor,
    KeyEncryptionKeyRing ring,
    IAuditLog auditLog,
    SecretRedactor redactor,
    IClock clock,
    ILogger<CredentialKeyRewrapper> logger)
{
    /// <summary>The action an audit row from a rotation is recorded under.</summary>
    public const string AuditAction = "inventory.credential-rewrap";

    /// <summary>
    /// How many rows are loaded at a time. Small enough that a rotation over a large estate never
    /// holds the whole table in memory, large enough that it is not a round trip per profile.
    /// </summary>
    private const int BatchSize = 100;

    /// <summary>Re-wraps every profile that is not already on the active key.</summary>
    public async Task<CredentialRewrapReport> RewrapAsync(CancellationToken cancellationToken)
    {
        int examined = 0;
        int rewrapped = 0;
        Guid after = Guid.Empty;

        try
        {
            while (true)
            {
                // Keyset by id rather than by offset: rows are being updated as this walks, and
                // the column it filters on is the one it is changing.
                List<CredentialProfile> batch = await context.CredentialProfiles
                    .Where(profile => profile.Id > after && profile.KeyId != ring.ActiveKeyId)
                    .OrderBy(profile => profile.Id)
                    .Take(BatchSize)
                    .ToListAsync(cancellationToken);

                if (batch.Count == 0)
                {
                    break;
                }

                examined += batch.Count;
                after = batch[^1].Id;

                foreach (CredentialProfile profile in batch)
                {
                    if (encryptor.TryRewrap(
                        profile.Ciphertext,
                        CredentialProfile.ContextFor(profile.Id),
                        out EnvelopeCiphertext moved))
                    {
                        profile.SetCiphertext(moved);
                        rewrapped++;
                    }
                }

                // updated_at is deliberately left alone. Nothing about the profile as an operator
                // understands it has changed, and a rotation that touched every row's timestamp
                // would look like an estate-wide edit in every list sorted by it.
                await context.SaveChangesAsync(cancellationToken);

                logger.LogInformation(
                    "Re-wrapped {Rewrapped} of {Examined} credential profiles so far.",
                    rewrapped,
                    examined);
            }
        }
        catch (Exception failure)
        {
            await RecordAsync(new CredentialRewrapReport(ring.ActiveKeyId, examined, rewrapped), false);

            logger.LogError(
                failure,
                "Key rotation stopped after re-wrapping {Rewrapped} profiles. Every key involved must "
                + "stay in the ring until a later run reports none left.",
                rewrapped);

            throw;
        }

        CredentialRewrapReport report = new(ring.ActiveKeyId, examined, rewrapped);

        await RecordAsync(report, true);

        logger.LogInformation(
            "Key rotation complete: {Rewrapped} of {Examined} credential profiles moved to the active key.",
            report.Rewrapped,
            report.Examined);

        return report;
    }

    /// <summary>
    /// Writes the audit row itself.
    /// </summary>
    /// <remarks>
    /// The middleware in <c>NetShield.Platform</c> records every state-changing API call, and this
    /// is not one — so the row is written here rather than the operation being moved onto the web
    /// surface in order to inherit it. <c>http_method</c> and <c>path</c> are required columns on
    /// a table shaped for requests; they say what actually ran instead of describing a request
    /// that never happened.
    /// </remarks>
    private async Task RecordAsync(CredentialRewrapReport report, bool succeeded)
    {
        DateTimeOffset now = clock.UtcNow;

        await auditLog.AppendAsync(
            new AuditEntry
            {
                Id = Guid.CreateVersion7(now),
                CreatedAt = now,

                // No actor. The command is run by whoever holds the host, and inventing a user
                // id for them would be the audit log asserting something it does not know.
                Action = AuditAction,
                TargetType = CredentialResolver.ResourceType,
                Outcome = succeeded ? AuditOutcome.Succeeded : AuditOutcome.Failed,
                Before = null,
                After = AuditPayload.Serialize(
                    new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        // A key id, not a key. It names which key is now active; it opens nothing.
                        ["activeKeyId"] = report.ActiveKeyId,
                        ["examined"] = report.Examined,
                        ["rewrapped"] = report.Rewrapped
                    },
                    redactor),
                HttpMethod = "COMMAND",
                Path = "NetShield.Web.Host --rewrap",
                StatusCode = succeeded ? 200 : 500
            },
            CancellationToken.None);
    }
}
