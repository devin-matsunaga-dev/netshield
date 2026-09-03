using NetShield.Contracts.Identity;

namespace NetShield.Platform.Authorization;

/// <summary>
/// Who is making the current request, as a handler sees them.
/// </summary>
/// <remarks>
/// Everything here is read from the session principal on the request. It is the actor an audit
/// row names and the role a resource check resolves against; it is never assembled from
/// anything the caller supplied in a body or a header.
/// </remarks>
public interface ICurrentUser
{
    /// <summary>Whether there is a session at all.</summary>
    bool IsAuthenticated { get; }

    /// <summary>The signed-in account, or <see langword="null"/> when the caller is anonymous.</summary>
    Guid? UserId { get; }

    /// <summary>The account name, or <see langword="null"/> when the caller is anonymous.</summary>
    string? Username { get; }

    /// <summary>The role the session carries, or <see langword="null"/> when it carries none.</summary>
    UserRole? Role { get; }

    /// <summary>
    /// The address the request arrived from, as text, or <see langword="null"/> when there is no
    /// connection to read it off — a background job, or a test.
    /// </summary>
    string? SourceIp { get; }

    /// <summary>Whether this session's role grants <paramref name="permission"/>.</summary>
    bool Has(Permission permission);
}
