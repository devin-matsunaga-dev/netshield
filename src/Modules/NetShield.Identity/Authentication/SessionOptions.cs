using System.ComponentModel.DataAnnotations;

namespace NetShield.Identity.Authentication;

/// <summary>
/// The shape of a signed-in session. Bound from <c>Identity:Session</c>.
/// </summary>
/// <remarks>
/// The cookie flags themselves are not configurable. <c>HttpOnly</c>, <c>Secure</c> and
/// <c>SameSite=Lax</c> are required by ARCHITECTURE.md §8 and WP-0.4, and a security property
/// that an environment file can turn off is one that will eventually be off in production.
/// </remarks>
public sealed class SessionOptions
{
    /// <summary>The configuration section this binds from.</summary>
    public const string SectionName = "Identity:Session";

    /// <summary>
    /// How long the session cookie authenticates for. Short, because the refresh token is what
    /// gives the session its length and the session cookie is what an XSS would steal.
    /// </summary>
    [Range(typeof(TimeSpan), "00:01:00", "08:00:00")]
    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// How long a refresh token remains usable. Rotation replaces it on every use, so this is the
    /// idle window rather than the session's total length.
    /// </summary>
    [Range(typeof(TimeSpan), "00:05:00", "90.00:00:00")]
    public TimeSpan RefreshLifetime { get; set; } = TimeSpan.FromDays(14);

    /// <summary>Consecutive failures before the account locks.</summary>
    [Range(1, 100)]
    public int MaxFailedLoginAttempts { get; set; } = 5;

    /// <summary>How long a locked account stays locked.</summary>
    [Range(typeof(TimeSpan), "00:01:00", "24:00:00")]
    public TimeSpan LockoutDuration { get; set; } = TimeSpan.FromMinutes(15);
}
