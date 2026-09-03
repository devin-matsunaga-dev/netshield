using FluentAssertions;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using NetShield.Contracts.Identity;

using NetShield.Platform;
using NetShield.Platform.Auditing;
using NetShield.Platform.Authorization;

namespace NetShield.UnitTests.Authorization;

/// <summary>
/// Covers what <c>AddNetShieldAuthorization</c> and <c>AddNetShieldAudit</c> leave behind in the
/// container — deny by default above all, since an endpoint that declares no policy has to be
/// refused rather than published.
/// </summary>
public sealed class AuthorizationRegistrationTests
{
    [Fact]
    public async Task TheFallbackPolicy_RequiresAnAuthenticatedUserWhoOwesNoPasswordChange()
    {
        using IHost host = BuildHost();

        AuthorizationPolicy? fallback = await Provider(host).GetFallbackPolicyAsync();

        fallback.Should().NotBeNull(
            "an endpoint that declares no authorization must be refused, not published");

        fallback!.Requirements.OfType<DenyAnonymousAuthorizationRequirement>().Should().ContainSingle();
        fallback.Requirements.OfType<PasswordChangeNotPendingRequirement>().Should().ContainSingle();
    }

    [Fact]
    public async Task TheDefaultPolicy_CarriesTheSameTwoRequirements()
    {
        using IHost host = BuildHost();

        AuthorizationPolicy policy = await Provider(host).GetDefaultPolicyAsync();

        policy.Requirements.Should().HaveCount(2);
    }

    [Fact]
    public async Task APermissionPolicy_IsMaterialisedOnDemand_CarryingAllThreeRequirements()
    {
        using IHost host = BuildHost();

        AuthorizationPolicy? policy =
            await Provider(host).GetPolicyAsync(PermissionPolicy.NameFor(Permission.InventoryWrite));

        policy.Should().NotBeNull();

        policy!.Requirements.OfType<PermissionRequirement>().Should()
            .ContainSingle().Which.Permission.Should().Be(Permission.InventoryWrite);

        // Naming a permission must not be a way to opt out of the other two.
        policy.Requirements.OfType<DenyAnonymousAuthorizationRequirement>().Should().ContainSingle();
        policy.Requirements.OfType<PasswordChangeNotPendingRequirement>().Should().ContainSingle();
    }

    [Fact]
    public async Task APolicyNameThatIsNotOurs_FallsThroughToTheDefaultProvider()
    {
        using IHost host = BuildHost();

        (await Provider(host).GetPolicyAsync("SomethingNobodyRegistered")).Should().BeNull();
    }

    [Fact]
    public void BothAuthorizationHandlers_AreRegistered()
    {
        using IHost host = BuildHost();

        IReadOnlyList<IAuthorizationHandler> handlers = [.. host.Services.GetServices<IAuthorizationHandler>()];

        handlers.OfType<PermissionAuthorizationHandler>().Should().ContainSingle();
        handlers.OfType<PasswordChangeNotPendingHandler>().Should().ContainSingle();
    }

    [Fact]
    public void TheAuditContextAndItsInterface_AreTheSameInstanceWithinARequest()
    {
        using IHost host = BuildHost();
        using IServiceScope scope = host.Services.CreateScope();

        // A handler enriching IAuditContext has to be enriching the object the middleware reads.
        scope.ServiceProvider.GetRequiredService<IAuditContext>()
            .Should().BeSameAs(scope.ServiceProvider.GetRequiredService<AuditContext>());
    }

    [Fact]
    public void TheAuditContext_IsNotSharedAcrossRequests()
    {
        using IHost host = BuildHost();
        using IServiceScope first = host.Services.CreateScope();
        using IServiceScope second = host.Services.CreateScope();

        first.ServiceProvider.GetRequiredService<IAuditContext>()
            .Should().NotBeSameAs(second.ServiceProvider.GetRequiredService<IAuditContext>());
    }

    private static IAuthorizationPolicyProvider Provider(IHost host) =>
        host.Services.GetRequiredService<IAuthorizationPolicyProvider>();

    private static IHost BuildHost()
    {
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());

        builder.AddNetShieldPlatform();
        builder.AddNetShieldAuthorization();
        builder.AddNetShieldAudit();

        return builder.Build();
    }
}
