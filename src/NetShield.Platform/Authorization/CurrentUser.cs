using System.Security.Claims;

using Microsoft.AspNetCore.Http;

using NetShield.Contracts.Identity;

namespace NetShield.Platform.Authorization;

/// <summary>
/// <see cref="ICurrentUser"/> over the ambient <see cref="HttpContext"/>.
/// </summary>
/// <remarks>
/// Registered as a singleton over <see cref="IHttpContextAccessor"/> rather than as a scoped
/// snapshot, because a background service resolving it outside a request has to see "nobody"
/// rather than a stale actor captured from whichever request created the scope.
/// </remarks>
internal sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public bool IsAuthenticated => Principal?.Identity is { IsAuthenticated: true };

    public Guid? UserId =>
        Guid.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out Guid id) ? id : null;

    public string? Username => Principal?.FindFirstValue(ClaimTypes.Name);

    public UserRole? Role => AuthorizationClaims.RoleOf(Principal);

    public string? SourceIp => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public bool Has(Permission permission) =>
        IsAuthenticated && Role is { } role && RolePermissions.Grants(role, permission);

    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;
}
