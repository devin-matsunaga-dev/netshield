using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NetShield.Contracts.Identity;

using NetShield.Identity.Passwords;
using NetShield.Identity.Persistence;
using NetShield.Identity.Users;

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

        user.PasswordHash = await hasher.HashAsync(request.NewPassword, cancellationToken);
        user.MustChangePassword = false;
        user.PasswordChangedAt = now;
        user.UpdatedAt = now;

        await sessions.RevokeAllForUserAsync(user.Id, cancellationToken);

        SessionGrant grant = sessions.Issue(user);

        await database.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Account {UserId} changed its password; every other session was revoked.",
            user.Id);

        return grant;
    }
}
