using Microsoft.Extensions.Logging;

using NetShield.Contracts.Identity;

using NetShield.Platform.Auditing;
using NetShield.Platform.Results;

namespace NetShield.Platform.Authorization;

/// <summary>
/// <see cref="IResourceGuard"/> over the current session, naming the refused resource on the
/// audit row as it goes.
/// </summary>
internal sealed class ResourceGuard(
    ICurrentUser user,
    IAuditContext audit,
    ILogger<ResourceGuard> logger) : IResourceGuard
{
    /// <summary>Returned for every refusal, so a caller learns nothing from which one it was.</summary>
    private static readonly Error Forbidden = Error.Forbidden(
        "authorization.forbidden",
        "You do not have permission to do that.");

    private static readonly Error Unauthenticated = Error.Unauthenticated(
        "identity.no-session",
        "You are not signed in.");

    public Result Require(Permission permission, string resourceType, string? resourceId = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(resourceType);

        // Named whatever the answer is: a refusal that does not say what was refused is a row
        // nobody can act on.
        audit.Target(resourceType, resourceId);

        if (!user.IsAuthenticated)
        {
            logger.LogWarning(
                "An anonymous caller reached a handler guarding {Permission} on {ResourceType}.",
                permission,
                resourceType);

            return Unauthenticated;
        }

        if (user.Has(permission))
        {
            return Result.Success;
        }

        logger.LogWarning(
            "Account {UserId} in role {Role} was refused {Permission} on {ResourceType} {ResourceId}.",
            user.UserId,
            user.Role,
            permission,
            resourceType,
            resourceId);

        return Forbidden;
    }
}
