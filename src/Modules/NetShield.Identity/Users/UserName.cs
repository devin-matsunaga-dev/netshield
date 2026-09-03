using System.Globalization;

namespace NetShield.Identity.Users;

/// <summary>
/// How a username is reduced to the form the unique index and every lookup use.
/// </summary>
public static class UserName
{
    /// <summary>
    /// Trims and lower-cases, invariantly. Invariant rather than current-culture because the
    /// Turkish dotless <c>ı</c> would otherwise make <c>ADMIN</c> and <c>admin</c> different
    /// accounts on a server whose locale nobody thought about.
    /// </summary>
    public static string Normalize(string username)
    {
        ArgumentNullException.ThrowIfNull(username);

        return username.Trim().ToLower(CultureInfo.InvariantCulture);
    }
}
