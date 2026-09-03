using Microsoft.Extensions.Logging;

using NetShield.Identity.Persistence;

namespace NetShield.Identity.Authentication;

/// <summary>
/// Ends a session: the refresh chain is revoked in the database and the endpoint clears the
/// cookies.
/// </summary>
/// <remarks>
/// It never fails. Signing out is idempotent, and a caller whose session had already expired
/// still gets its cookies cleared rather than a 401 it can do nothing about.
/// </remarks>
internal sealed class LogoutHandler(
    IdentityDbContext database,
    SessionService sessions,
    ILogger<LogoutHandler> logger)
{
    internal async Task HandleAsync(Guid? sessionId, string? presentedToken, CancellationToken cancellationToken)
    {
        // The claim is the authority; the cookie is the fallback for a session cookie that has
        // already expired while the refresh cookie is still on the browser.
        Guid? chain = sessionId;

        if (chain is null && !string.IsNullOrEmpty(presentedToken))
        {
            chain = await sessions.SessionIdForAsync(presentedToken, cancellationToken);
        }

        if (chain is null)
        {
            return;
        }

        await sessions.RevokeChainAsync(chain.Value, cancellationToken);
        await database.SaveChangesAsync(cancellationToken);

        logger.LogInformation("A session was signed out and its refresh chain revoked.");
    }
}
