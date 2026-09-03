using Microsoft.AspNetCore.Http;

namespace NetShield.Identity.Authentication;

/// <summary>
/// The two cookies a NetShield session is carried in, and the one place their flags are set.
/// </summary>
/// <remarks>
/// <para>
/// The split is deliberate. The session cookie is presented on every request and is therefore the
/// one an attacker has the most chances to obtain; it is short-lived. The refresh cookie is
/// presented only to the refresh endpoint — <c>Path</c> keeps the browser from sending it
/// anywhere else — and is the only thing that can mint a new session.
/// </para>
/// <para>
/// <c>SameSite=Lax</c> rather than <c>Strict</c> because ARCHITECTURE.md §8 says so, and because
/// <c>Strict</c> would drop the session on a top-level navigation into the app from an alert
/// email, which is the single most common way an operator arrives at it.
/// </para>
/// </remarks>
public static class SessionCookies
{
    /// <summary>The authentication cookie the session is read from.</summary>
    public const string SessionCookieName = "netshield_session";

    /// <summary>The cookie carrying the opaque refresh token.</summary>
    public const string RefreshCookieName = "netshield_refresh";

    /// <summary>
    /// The only path the browser sends <see cref="RefreshCookieName"/> to. Every other endpoint,
    /// including a compromised one, never sees it.
    /// </summary>
    public const string RefreshCookiePath = "/api/v1/auth/refresh";

    /// <summary>Writes the refresh cookie for a freshly issued token.</summary>
    public static void WriteRefresh(HttpResponse response, string token, DateTimeOffset expiresAt)
    {
        ArgumentNullException.ThrowIfNull(response);

        response.Cookies.Append(RefreshCookieName, token, BuildRefreshOptions(expiresAt));
    }

    /// <summary>Removes the refresh cookie, matching the attributes it was written with.</summary>
    public static void ClearRefresh(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        // Delete has to repeat Path and SameSite or the browser treats it as a different cookie
        // and leaves the original in place.
        response.Cookies.Delete(RefreshCookieName, BuildRefreshOptions(expiresAt: null));
    }

    private static CookieOptions BuildRefreshOptions(DateTimeOffset? expiresAt) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        Path = RefreshCookiePath,
        IsEssential = true,
        Expires = expiresAt
    };
}
