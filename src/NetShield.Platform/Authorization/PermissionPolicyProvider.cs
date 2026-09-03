using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

using NetShield.Contracts.Identity;

namespace NetShield.Platform.Authorization;

/// <summary>
/// Materialises a policy for each <see cref="Permission"/> the first time an endpoint asks for
/// one, and defers everything else to the default provider.
/// </summary>
/// <remarks>
/// Every generated policy requires an authenticated user, the permission, and that no password
/// change is outstanding — the same three the default policy carries, so that an endpoint which
/// names a permission does not thereby opt out of the other two.
/// </remarks>
internal sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallback = new(options);

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (PermissionPolicy.PermissionFor(policyName) is not { } permission)
        {
            return _fallback.GetPolicyAsync(policyName);
        }

        AuthorizationPolicy policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(permission))
            .AddRequirements(new PasswordChangeNotPendingRequirement())
            .Build();

        return Task.FromResult<AuthorizationPolicy?>(policy);
    }
}
