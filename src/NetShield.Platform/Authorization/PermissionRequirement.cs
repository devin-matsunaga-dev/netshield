using Microsoft.AspNetCore.Authorization;

using NetShield.Contracts.Identity;

namespace NetShield.Platform.Authorization;

/// <summary>
/// Requires that the session's role grants <see cref="Permission"/>.
/// </summary>
/// <param name="Permission">The capability the endpoint needs.</param>
public sealed record PermissionRequirement(Permission Permission) : IAuthorizationRequirement;
