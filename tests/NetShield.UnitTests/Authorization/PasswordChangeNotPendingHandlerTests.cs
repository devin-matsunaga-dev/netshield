using System.Security.Claims;

using FluentAssertions;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;

using NetShield.Platform.Authorization;

namespace NetShield.UnitTests.Authorization;

/// <summary>
/// Covers the requirement that closes the gap STATUS.md recorded against WP-0.4: a user who owes
/// a password change is refused everywhere except the routes that let them make it.
/// </summary>
public sealed class PasswordChangeNotPendingHandlerTests
{
    [Fact]
    public async Task HandleAsync_ASessionOwingNothing_Succeeds()
    {
        AuthorizationHandlerContext context = ContextFor(pending: false, exempt: false);

        await Handler().HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_ASessionOwingAChange_DoesNotSucceed()
    {
        AuthorizationHandlerContext context = ContextFor(pending: true, exempt: false);

        await Handler().HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_ASessionOwingAChange_OnAnExemptEndpoint_Succeeds()
    {
        AuthorizationHandlerContext context = ContextFor(pending: true, exempt: true);

        await Handler().HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_ASessionOwingAChange_WithNoEndpointToRead_DoesNotSucceed()
    {
        // No HttpContext resource means nothing can prove the route is exempt, and a requirement
        // that cannot prove an exemption has to refuse.
        AuthorizationHandlerContext context = new(
            [new PasswordChangeNotPendingRequirement()],
            PrincipalWith(pending: true),
            resource: null);

        await Handler().HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    private static PasswordChangeNotPendingHandler Handler() =>
        new(NullLogger<PasswordChangeNotPendingHandler>.Instance);

    private static AuthorizationHandlerContext ContextFor(bool pending, bool exempt)
    {
        DefaultHttpContext http = new();

        Endpoint endpoint = new(
            requestDelegate: null,
            new EndpointMetadataCollection(exempt ? [new AllowPendingPasswordChangeAttribute()] : []),
            displayName: "probe");

        http.Features.Set<IEndpointFeature>(new EndpointFeature { Endpoint = endpoint });

        return new AuthorizationHandlerContext(
            [new PasswordChangeNotPendingRequirement()],
            PrincipalWith(pending),
            http);
    }

    private static ClaimsPrincipal PrincipalWith(bool pending)
    {
        List<Claim> claims = [new Claim(ClaimTypes.Name, "someone")];

        if (pending)
        {
            claims.Add(new Claim(
                AuthorizationClaims.PasswordChangeRequired,
                AuthorizationClaims.PasswordChangeRequiredValue));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test"));
    }

    private sealed class EndpointFeature : IEndpointFeature
    {
        public Endpoint? Endpoint { get; set; }
    }
}
