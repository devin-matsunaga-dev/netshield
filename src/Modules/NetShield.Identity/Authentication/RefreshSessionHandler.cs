using Microsoft.Extensions.Logging;

using NetShield.Identity.Persistence;

using NetShield.Platform.Results;

namespace NetShield.Identity.Authentication;

/// <summary>
/// Exchanges the refresh cookie for a new session, rotating the token as it goes.
/// </summary>
internal sealed class RefreshSessionHandler(
    IdentityDbContext database,
    SessionService sessions,
    ILogger<RefreshSessionHandler> logger)
{
    internal async Task<Result<SessionGrant>> HandleAsync(string? presentedToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(presentedToken))
        {
            return AuthenticationErrors.NoSession;
        }

        SessionGrant? grant = await sessions.RotateAsync(presentedToken, cancellationToken);

        // Rotation revokes on the way through — including the whole chain when it detects a token
        // being replayed — so the failure path has changes to save just as the success path does.
        await database.SaveChangesAsync(cancellationToken);

        if (grant is null)
        {
            logger.LogWarning("Refresh refused: the presented token is unknown, expired or already spent.");
            return AuthenticationErrors.InvalidCredentials;
        }

        logger.LogInformation("Session refreshed for account {UserId}.", grant.User.Id);

        return grant;
    }
}
