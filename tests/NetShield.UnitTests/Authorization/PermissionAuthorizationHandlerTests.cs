using System.Security.Claims;

using FluentAssertions;

using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging.Abstractions;

using NetShield.Contracts.Identity;

using NetShield.Platform.Authorization;

namespace NetShield.UnitTests.Authorization;

/// <summary>
/// Covers the handler that turns a role claim into a permission decision. CONVENTIONS.md §7
/// names RBAC checks as required coverage.
/// </summary>
public sealed class PermissionAuthorizationHandlerTests
{
    [Fact]
    public async Task HandleAsync_ARoleThatHoldsThePermission_Succeeds()
    {
        AuthorizationHandlerContext context = ContextFor(UserRole.Operator, Permission.InventoryWrite);

        await Handler().HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_ARoleThatDoesNotHoldThePermission_DoesNotSucceed()
    {
        AuthorizationHandlerContext context = ContextFor(UserRole.Analyst, Permission.InventoryWrite);

        await Handler().HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_AnAnonymousCaller_DoesNotSucceed()
    {
        AuthorizationHandlerContext context = new(
            [new PermissionRequirement(Permission.InventoryRead)],
            new ClaimsPrincipal(new ClaimsIdentity()),
            resource: null);

        await Handler().HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_ASessionCarryingNoRole_DoesNotSucceed()
    {
        ClaimsPrincipal principal = new(new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "someone")],
            authenticationType: "Test"));

        AuthorizationHandlerContext context = new(
            [new PermissionRequirement(Permission.InventoryRead)],
            principal,
            resource: null);

        await Handler().HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_ASessionClaimingAPermissionDirectly_DoesNotSucceed()
    {
        // The one claim that decides anything is the role. A permission the client put on its own
        // cookie is a permission nothing reads (ARCHITECTURE.md §8).
        ClaimsPrincipal principal = new(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Role, UserRole.ReadOnly.ToString()),
                new Claim("permission", Permission.InventoryWrite.ToString())
            ],
            authenticationType: "Test",
            ClaimTypes.Name,
            ClaimTypes.Role));

        AuthorizationHandlerContext context = new(
            [new PermissionRequirement(Permission.InventoryWrite)],
            principal,
            resource: null);

        await Handler().HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    private static PermissionAuthorizationHandler Handler() =>
        new(NullLogger<PermissionAuthorizationHandler>.Instance);

    private static AuthorizationHandlerContext ContextFor(UserRole role, Permission permission)
    {
        ClaimsPrincipal principal = new(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, role.ToString())],
            authenticationType: "Test",
            ClaimTypes.Name,
            ClaimTypes.Role));

        return new AuthorizationHandlerContext([new PermissionRequirement(permission)], principal, resource: null);
    }
}
