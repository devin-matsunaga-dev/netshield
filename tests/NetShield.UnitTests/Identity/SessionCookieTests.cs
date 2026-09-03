using FluentAssertions;

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using NetShield.Identity;
using NetShield.Identity.Authentication;
using NetShield.Identity.Persistence;

using NetShield.Platform;

namespace NetShield.UnitTests.Identity;

/// <summary>
/// WP-0.4 and ARCHITECTURE.md §8 name three cookie attributes by hand. They are asserted here,
/// at the registration, so a later package cannot quietly drop one.
/// </summary>
public sealed class SessionCookieTests
{
    [Fact]
    public void SessionCookie_IsHttpOnlySecureAndSameSiteLax()
    {
        using IHost host = BuildHost();

        CookieAuthenticationOptions options = host.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);

        options.Cookie.Name.Should().Be(SessionCookies.SessionCookieName);
        options.Cookie.HttpOnly.Should().BeTrue();
        options.Cookie.SecurePolicy.Should().Be(CookieSecurePolicy.Always);
        options.Cookie.SameSite.Should().Be(SameSiteMode.Lax);
    }

    [Fact]
    public void SessionCookie_DoesNotSlide()
    {
        using IHost host = BuildHost();

        CookieAuthenticationOptions options = host.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);

        options.SlidingExpiration.Should().BeFalse(
            "the refresh token extends a session, and it rotates while a sliding cookie would not");
    }

    [Fact]
    public void SessionCookie_ExpiresOnTheConfiguredSessionLifetime()
    {
        using IHost host = BuildHost(new Dictionary<string, string?>
        {
            ["Identity:Session:SessionLifetime"] = "00:07:00"
        });

        CookieAuthenticationOptions options = host.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);

        options.ExpireTimeSpan.Should().Be(TimeSpan.FromMinutes(7));
    }

    [Fact]
    public void WriteRefresh_SetsTheFlagsAndScopesTheCookieToTheRefreshEndpoint()
    {
        DefaultHttpContext context = new();

        SessionCookies.WriteRefresh(context.Response, "a-token", DateTimeOffset.UtcNow.AddDays(14));

        string cookie = context.Response.Headers.SetCookie.Single()!;

        cookie.Should().StartWith($"{SessionCookies.RefreshCookieName}=a-token")
            .And.Contain("httponly")
            .And.Contain("secure")
            .And.Contain("samesite=lax")
            .And.Contain($"path={SessionCookies.RefreshCookiePath}")
            .And.Contain("expires=");
    }

    [Fact]
    public void ClearRefresh_RepeatsThePathSoTheBrowserRemovesTheSameCookie()
    {
        DefaultHttpContext context = new();

        SessionCookies.ClearRefresh(context.Response);

        string cookie = context.Response.Headers.SetCookie.Single()!;

        cookie.Should().StartWith($"{SessionCookies.RefreshCookieName}=")
            .And.Contain($"path={SessionCookies.RefreshCookiePath}")
            .And.Contain("expires=Thu, 01 Jan 1970");
    }

    private static IHost BuildHost(Dictionary<string, string?>? configuration = null)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();

        if (configuration is not null)
        {
            builder.Configuration.AddInMemoryCollection(configuration);
        }

        // No connection string: registering a context does not open one, and nothing here queries.
        builder.Services.AddDbContext<IdentityDbContext>(options => options.UseIdentityConventions());

        builder.AddNetShieldPlatform();
        builder.AddNetShieldIdentity();

        return builder.Build();
    }
}
