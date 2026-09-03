using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace NetShield.Platform.Authorization;

/// <summary>
/// Refuses every authenticated request from a session that still owes a password change, except
/// on an endpoint marked <see cref="AllowPendingPasswordChangeAttribute"/>.
/// </summary>
internal sealed class PasswordChangeNotPendingHandler(ILogger<PasswordChangeNotPendingHandler> logger)
    : AuthorizationHandler<PasswordChangeNotPendingRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PasswordChangeNotPendingRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        if (!AuthorizationClaims.PasswordChangeIsPending(context.User))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        // Endpoint-routed authorization hands the HttpContext through as the resource, which is
        // the only way this handler can see what the request was routed to.
        if (context.Resource is HttpContext http
            && http.GetEndpoint()?.Metadata.GetMetadata<AllowPendingPasswordChangeAttribute>() is not null)
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        logger.LogInformation("Request refused: the session still owes a password change.");

        return Task.CompletedTask;
    }
}
