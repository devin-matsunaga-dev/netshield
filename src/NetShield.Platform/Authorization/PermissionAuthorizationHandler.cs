using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;

using NetShield.Contracts.Identity;

namespace NetShield.Platform.Authorization;

/// <summary>
/// Decides a <see cref="PermissionRequirement"/> by resolving the session's role through
/// <see cref="RolePermissions"/>.
/// </summary>
/// <remarks>
/// The principal is asked for its role and for nothing else. A permission claim on the cookie
/// would be a claim the client could choose, and ARCHITECTURE.md §8 says never to trust one.
/// </remarks>
internal sealed class PermissionAuthorizationHandler(ILogger<PermissionAuthorizationHandler> logger)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        if (context.User.Identity is not { IsAuthenticated: true })
        {
            return Task.CompletedTask;
        }

        UserRole? role = AuthorizationClaims.RoleOf(context.User);

        if (role is null)
        {
            // A session with no readable role is a session minted by something that did not
            // follow the claim contract. It holds nothing, and that is worth an operator's time.
            logger.LogWarning("A session carries no readable role claim; {Permission} refused.", requirement.Permission);
            return Task.CompletedTask;
        }

        if (RolePermissions.Grants(role.Value, requirement.Permission))
        {
            context.Succeed(requirement);
        }
        else
        {
            logger.LogInformation(
                "Role {Role} does not hold {Permission}.",
                role.Value,
                requirement.Permission);
        }

        return Task.CompletedTask;
    }
}
