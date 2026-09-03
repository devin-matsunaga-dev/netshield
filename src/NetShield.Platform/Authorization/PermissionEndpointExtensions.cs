using Microsoft.AspNetCore.Builder;

using NetShield.Contracts.Identity;

namespace NetShield.Platform.Authorization;

/// <summary>
/// How an endpoint declares what it needs. The endpoint-level half of ARCHITECTURE.md §8; the
/// module-level half is <see cref="IResourceGuard"/>.
/// </summary>
public static class PermissionEndpointExtensions
{
    /// <summary>Refuses the request unless the session's role grants <paramref name="permission"/>.</summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, Permission permission)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.RequireAuthorization(PermissionPolicy.NameFor(permission));

        return builder;
    }

    /// <summary>
    /// Lets a session that still owes a password change reach this endpoint. Reserved for the
    /// handful of routes such a user must be able to use to get out of that state.
    /// </summary>
    public static TBuilder AllowsPendingPasswordChange<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.WithMetadata(new AllowPendingPasswordChangeAttribute());

        return builder;
    }
}
