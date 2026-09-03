using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NetShield.Contracts.Identity;

using NetShield.Identity.Passwords;
using NetShield.Identity.Persistence;
using NetShield.Identity.Users;

using NetShield.Platform.Auditing;
using NetShield.Platform.Results;
using NetShield.Platform.Time;

namespace NetShield.Identity.Authentication;

/// <summary>
/// Replaces the signed-in account's password, and is the way out of a forced first-run change.
/// </summary>
/// <remarks>
/// Every other session the account holds is revoked and the caller is given a fresh one. A
/// password change is what someone does after suspecting their password is known, and it would
/// be worth very little if it left the other sessions signed in.
/// </remarks>
internal sealed class ChangePasswordHandler(
    IdentityDbContext database,
    IPasswordHasher hasher,
    PasswordPolicy policy,
    SessionService sessions,
    IAuditContext audit,
    IClock clock,
    ILogger<ChangePasswordHandler> logger)
{
    internal async Task<Result<SessionGrant>> HandleAsync(
        Guid? userId,
        ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (userId is null)
        {
            return AuthenticationErrors.NoSession;
        }

        User? user = await database.Users
            .SingleOrDefaultAsync(candidate => candidate.Id == userId.Value, cancellationToken);

        if (user is not { IsActive: true })
        {
            return AuthenticationErrors.NoSession;
        }

        audit.Actor(user.Id, user.Username, user.Role);
        audit.Target("user", user.Id.ToString());

        PasswordVerification current =
            await hasher.VerifyAsync(request.CurrentPassword, user.PasswordHash, cancellationToken);

        if (!current.IsMatch)
        {
            logger.LogWarning(
                "Password change refused for account {UserId}: the current password did not match.",
                user.Id);

            return AuthenticationErrors.CurrentPasswordInvalid;
        }

        Result allowed = policy.Check(request.NewPassword, user.Username, user.Email);

        if (!allowed.IsSuccess)
        {
            return allowed.Error;
        }

        PasswordVerification unchanged =
            await hasher.VerifyAsync(request.NewPassword, user.PasswordHash, cancellationToken);

        if (unchanged.IsMatch)
        {
            return AuthenticationErrors.PasswordUnchanged;
        }

        DateTimeOffset now = clock.UtcNow;

        // Neither snapshot carries a hash, a password or a token — only the facts about the
        // account that changed. SPEC.md §5 covers the database as well as the log, and this is a
        // table nothing can ever go back and correct.
        //
        // The member names avoid the word the redactor treats as a secret: it redacts by
        // property name and does not stop to consider that a boolean cannot be a password, so a
        // member called "mustChangePassword" would be stored as [REDACTED] and say nothing. The
        // row already knows it is about a password change; these say what changed.
        Dictionary<string, object?> before = new(StringComparer.Ordinal)
        {
            ["changeRequired"] = user.MustChangePassword,
            ["changedAt"] = user.PasswordChangedAt
        };

        user.PasswordHash = await hasher.HashAsync(request.NewPassword, cancellationToken);
        user.MustChangePassword = false;
        user.PasswordChangedAt = now;
        user.UpdatedAt = now;

        await sessions.RevokeAllForUserAsync(user.Id, cancellationToken);

        SessionGrant grant = sessions.Issue(user);

        audit.Snapshot(before, new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["changeRequired"] = user.MustChangePassword,
            ["changedAt"] = user.PasswordChangedAt
        });

        await database.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Account {UserId} changed its password; every other session was revoked.",
            user.Id);

        return grant;
    }
}
