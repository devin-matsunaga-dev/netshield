using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using NetShield.Contracts.Identity;

using NetShield.Identity.Persistence;
using NetShield.Identity.Users;

using NetShield.Platform.Authorization;
using NetShield.Platform.Time;

namespace NetShield.Identity.Authentication;

/// <summary>
/// Issues, rotates and revokes the refresh-token chains behind a session.
/// </summary>
/// <remarks>
/// It writes rows and does not save them. The caller owns the transaction, because a sign-in also
/// clears a lockout counter and a password change also revokes every other session, and neither
/// may half-happen.
/// </remarks>
internal sealed class SessionService(IdentityDbContext database, IClock clock, IOptions<SessionOptions> options)
{
    /// <summary>
    /// Adds a token starting a new chain, and returns the grant the endpoint writes cookies from.
    /// </summary>
    internal SessionGrant Issue(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        Guid sessionId = Guid.CreateVersion7();

        return Add(user, sessionId, replacing: null);
    }

    /// <summary>
    /// Exchanges <paramref name="presentedToken"/> for its successor, revoking it in the process.
    /// </summary>
    /// <remarks>
    /// Presenting a token that was already spent is the signature of a stolen cookie being
    /// replayed, so the whole chain is revoked rather than only the token: the legitimate holder
    /// is signed out too, which is the intended outcome when one of the two is an attacker.
    /// </remarks>
    internal async Task<SessionGrant?> RotateAsync(string presentedToken, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(presentedToken);

        DateTimeOffset now = clock.UtcNow;
        string hash = RefreshTokenGenerator.Hash(presentedToken);

        RefreshToken? token = await database.RefreshTokens
            .Include(candidate => candidate.User)
            .SingleOrDefaultAsync(candidate => candidate.TokenHash == hash, cancellationToken);

        if (token is null)
        {
            return null;
        }

        if (token.RevokedAt is not null)
        {
            await RevokeChainAsync(token.SessionId, cancellationToken);
            return null;
        }

        if (!token.IsActive(now) || token.User is not { IsActive: true } user || user.IsLockedOut(now))
        {
            return null;
        }

        SessionGrant grant = Add(user, token.SessionId, replacing: token);

        return grant;
    }

    /// <summary>Ends every session in a chain. Used by logout and by reuse detection.</summary>
    internal async Task RevokeChainAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.UtcNow;

        List<RefreshToken> chain = await database.RefreshTokens
            .Where(token => token.SessionId == sessionId && token.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (RefreshToken token in chain)
        {
            token.RevokedAt = now;
        }
    }

    /// <summary>Ends every session an account holds. Used by a password change.</summary>
    internal async Task RevokeAllForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.UtcNow;

        List<RefreshToken> tokens = await database.RefreshTokens
            .Where(token => token.UserId == userId && token.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (RefreshToken token in tokens)
        {
            token.RevokedAt = now;
        }
    }

    /// <summary>Finds the live chain a presented token belongs to, without spending the token.</summary>
    internal async Task<Guid?> SessionIdForAsync(string presentedToken, CancellationToken cancellationToken)
    {
        string hash = RefreshTokenGenerator.Hash(presentedToken);

        RefreshToken? token = await database.RefreshTokens
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.TokenHash == hash, cancellationToken);

        return token?.SessionId;
    }

    private SessionGrant Add(User user, Guid sessionId, RefreshToken? replacing)
    {
        DateTimeOffset now = clock.UtcNow;

        string token = RefreshTokenGenerator.Create();

        RefreshToken issued = new()
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            TokenHash = RefreshTokenGenerator.Hash(token),
            SessionId = sessionId,
            CreatedAt = now,
            ExpiresAt = now + options.Value.RefreshLifetime
        };

        database.RefreshTokens.Add(issued);

        if (replacing is not null)
        {
            replacing.RevokedAt = now;
            replacing.ReplacedByTokenId = issued.Id;
        }

        return new SessionGrant(
            ToAuthenticatedUser(user),
            SessionClaims.CreatePrincipal(user, sessionId, CookieAuthenticationDefaults.AuthenticationScheme),
            token,
            issued.ExpiresAt);
    }

    /// <summary>The response shape for <paramref name="user"/>. Carries no hash and no token.</summary>
    internal static AuthenticatedUser ToAuthenticatedUser(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        return new AuthenticatedUser(
            user.Id,
            user.Username,
            user.DisplayName,
            user.Role,
            user.MustChangePassword,
            PermissionsFor(user.Role));
    }

    /// <summary>
    /// What <paramref name="role"/> may do, in a stable order, for the client to draw from.
    /// </summary>
    /// <remarks>
    /// Read from <see cref="RolePermissions"/> rather than restated here, so that the table
    /// authorization consults on every request is the same table the client is told about. The
    /// order is the declaration order of <see cref="Permission"/> so that two responses for the
    /// same role are byte-identical — a set's enumeration order is not a promise.
    /// </remarks>
    private static IReadOnlyList<Permission> PermissionsFor(UserRole role)
    {
        IReadOnlySet<Permission> granted = RolePermissions.For(role);

        return [.. Enum.GetValues<Permission>().Where(granted.Contains)];
    }
}
