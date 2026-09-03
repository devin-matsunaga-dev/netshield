using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NetShield.Contracts.Identity;

using NetShield.Identity.Passwords;
using NetShield.Identity.Persistence;
using NetShield.Identity.Users;

using NetShield.Platform.Results;
using NetShield.Platform.Time;

namespace NetShield.Identity.Authentication;

/// <summary>
/// Verifies a username and password and, on success, produces the session grant the endpoint
/// writes cookies from.
/// </summary>
/// <remarks>
/// Every refusal returns <see cref="AuthenticationErrors.InvalidCredentials"/> and every path
/// pays for one Argon2id verification, so neither the body nor the timing of a 401 distinguishes
/// an unknown username from a wrong password, a disabled account or a locked one.
/// </remarks>
internal sealed class LoginHandler(
    IdentityDbContext database,
    IPasswordHasher hasher,
    DecoyPasswordHash decoy,
    SessionService sessions,
    IClock clock,
    IOptions<SessionOptions> options,
    ILogger<LoginHandler> logger)
{
    internal async Task<Result<SessionGrant>> HandleAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        DateTimeOffset now = clock.UtcNow;
        string normalized = UserName.Normalize(request.Username);

        User? user = await database.Users
            .SingleOrDefaultAsync(candidate => candidate.NormalizedUsername == normalized, cancellationToken);

        if (user is null)
        {
            await hasher.VerifyAsync(request.Password, await decoy.ValueAsync(), cancellationToken);

            logger.LogInformation("Sign-in refused: no account matches the username presented.");
            return AuthenticationErrors.InvalidCredentials;
        }

        PasswordVerification verification =
            await hasher.VerifyAsync(request.Password, user.PasswordHash, cancellationToken);

        if (!user.IsActive)
        {
            logger.LogWarning("Sign-in refused for disabled account {UserId}.", user.Id);
            return AuthenticationErrors.InvalidCredentials;
        }

        if (user.IsLockedOut(now))
        {
            // Recorded here and nowhere the caller can see it. WP-0.4 requires the 401 to be
            // indistinguishable; an operator still has to be able to tell a locked account from a
            // forgotten password when someone rings up about it.
            logger.LogWarning(
                "Sign-in refused for locked account {UserId}; the lockout lapses at {LockedOutUntil:o}.",
                user.Id,
                user.LockedOutUntil);

            return AuthenticationErrors.InvalidCredentials;
        }

        if (!verification.IsMatch)
        {
            await RecordFailureAsync(user, now, cancellationToken);
            return AuthenticationErrors.InvalidCredentials;
        }

        return await GrantAsync(user, verification, request.Password, now, cancellationToken);
    }

    private async Task RecordFailureAsync(User user, DateTimeOffset now, CancellationToken cancellationToken)
    {
        SessionOptions session = options.Value;

        user.FailedLoginAttempts++;
        user.UpdatedAt = now;

        if (user.FailedLoginAttempts >= session.MaxFailedLoginAttempts)
        {
            user.LockedOutUntil = now + session.LockoutDuration;

            // Cleared rather than left at the maximum, so that a lockout which lapses returns the
            // account to a full allowance instead of locking again on the next single mistake.
            user.FailedLoginAttempts = 0;

            logger.LogWarning(
                "Account {UserId} locked after {Attempts} consecutive failed sign-ins; locked until {LockedOutUntil:o}.",
                user.Id,
                session.MaxFailedLoginAttempts,
                user.LockedOutUntil);
        }
        else
        {
            logger.LogInformation(
                "Sign-in refused for account {UserId}: wrong password, attempt {Attempts} of {MaxAttempts}.",
                user.Id,
                user.FailedLoginAttempts,
                session.MaxFailedLoginAttempts);
        }

        await database.SaveChangesAsync(cancellationToken);
    }

    private async Task<Result<SessionGrant>> GrantAsync(
        User user,
        PasswordVerification verification,
        string password,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        user.FailedLoginAttempts = 0;
        user.LockedOutUntil = null;
        user.LastLoginAt = now;
        user.UpdatedAt = now;

        if (verification.NeedsRehash)
        {
            // The one moment the plaintext is available with the account already proven. Costs a
            // second hash on one sign-in and leaves the row at the current work factor.
            user.PasswordHash = await hasher.HashAsync(password, cancellationToken);

            logger.LogInformation(
                "Rehashed the stored password for account {UserId} at the current work factor.",
                user.Id);
        }

        SessionGrant grant = sessions.Issue(user);

        await database.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Account {UserId} signed in.", user.Id);

        return grant;
    }
}
