using System.Net;
using System.Security.Claims;

using FluentAssertions;

using Microsoft.AspNetCore.Http;

using NetShield.Contracts.Identity;

using NetShield.Platform.Authorization;

namespace NetShield.UnitTests.Authorization;

/// <summary>Covers what a handler learns about the caller, and what it does not.</summary>
public sealed class CurrentUserTests
{
    [Fact]
    public void AnAuthenticatedSession_ReportsItsActorAndItsPermissions()
    {
        Guid id = Guid.CreateVersion7();
        ICurrentUser user = For(context =>
        {
            context.User = Principal(id, "kim", UserRole.Operator);
            context.Connection.RemoteIpAddress = IPAddress.Parse("10.20.30.40");
        });

        user.IsAuthenticated.Should().BeTrue();
        user.UserId.Should().Be(id);
        user.Username.Should().Be("kim");
        user.Role.Should().Be(UserRole.Operator);
        user.SourceIp.Should().Be("10.20.30.40");
        user.Has(Permission.InventoryWrite).Should().BeTrue();
        user.Has(Permission.CredentialsManage).Should().BeFalse();
    }

    [Fact]
    public void AnAnonymousRequest_ReportsNobodyAndHoldsNothing()
    {
        ICurrentUser user = For(context => context.User = new ClaimsPrincipal(new ClaimsIdentity()));

        user.IsAuthenticated.Should().BeFalse();
        user.UserId.Should().BeNull();
        user.Username.Should().BeNull();
        user.Role.Should().BeNull();
        user.Has(Permission.InventoryRead).Should().BeFalse();
    }

    [Fact]
    public void OutsideARequest_ReportsNobody()
    {
        // Resolved by a background service, this has to say "nobody" rather than hand back an
        // actor captured from whichever request happened to create the scope.
        ICurrentUser user = new CurrentUser(new HttpContextAccessor());

        user.IsAuthenticated.Should().BeFalse();
        user.UserId.Should().BeNull();
        user.SourceIp.Should().BeNull();
        user.Has(Permission.InventoryRead).Should().BeFalse();
    }

    private static ICurrentUser For(Action<HttpContext> arrange)
    {
        DefaultHttpContext context = new();
        arrange(context);

        return new CurrentUser(new HttpContextAccessor { HttpContext = context });
    }

    private static ClaimsPrincipal Principal(Guid id, string username, UserRole role) =>
        new(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, id.ToString()),
                new Claim(ClaimTypes.Name, username),
                new Claim(ClaimTypes.Role, role.ToString())
            ],
            authenticationType: "Test",
            ClaimTypes.Name,
            ClaimTypes.Role));
}
