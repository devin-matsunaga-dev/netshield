using System.Net;

using FluentAssertions;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace NetShield.UnitTests.ServiceDefaults;

/// <summary>
/// Covers the liveness and readiness split that <c>NetShield.ServiceDefaults</c> maps for every
/// service. A real host on an ephemeral loopback port; no container and no external dependency.
/// </summary>
public sealed class HealthEndpointTests
{
    private const string Liveness = "/health";
    private const string Readiness = "/health/ready";

    [Fact]
    public async Task Health_ReportsHealthy_WhenTheProcessIsUp()
    {
        await using HealthProbeHost host = await HealthProbeHost.StartAsync(TestContext.Current.CancellationToken);

        (await host.GetAsync(Liveness, TestContext.Current.CancellationToken)).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HealthReady_ReportsHealthy_WhenEveryDependencyPasses()
    {
        await using HealthProbeHost host = await HealthProbeHost.StartAsync(TestContext.Current.CancellationToken, HealthStatus.Healthy);

        (await host.GetAsync(Readiness, TestContext.Current.CancellationToken)).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HealthReady_ReportsUnhealthy_WhenADependencyFails()
    {
        await using HealthProbeHost host = await HealthProbeHost.StartAsync(TestContext.Current.CancellationToken, HealthStatus.Unhealthy);

        (await host.GetAsync(Readiness, TestContext.Current.CancellationToken)).Should().Be(HttpStatusCode.ServiceUnavailable,
            "readiness covers PostgreSQL and Redis, so a store that cannot answer must fail it");
    }

    [Fact]
    public async Task Health_StaysHealthy_WhenADependencyFails_BecauseLivenessIsNotReadiness()
    {
        await using HealthProbeHost host = await HealthProbeHost.StartAsync(TestContext.Current.CancellationToken, HealthStatus.Unhealthy);

        (await host.GetAsync(Liveness, TestContext.Current.CancellationToken)).Should().Be(HttpStatusCode.OK,
            "an unreachable database is not a reason to restart the process");
    }

    [Fact]
    public async Task NeitherEndpoint_IsMapped_OutsideDevelopment()
    {
        await using HealthProbeHost host = await HealthProbeHost.StartAsync(TestContext.Current.CancellationToken, environment: "Production");

        (await host.GetAsync(Liveness, TestContext.Current.CancellationToken)).Should().Be(HttpStatusCode.NotFound);
        (await host.GetAsync(Readiness, TestContext.Current.CancellationToken)).Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>A host wired exactly as a NetShield service is, with one optional dependency check.</summary>
    private sealed class HealthProbeHost(WebApplication application, HttpClient client) : IAsyncDisposable
    {
        public static async Task<HealthProbeHost> StartAsync(
            CancellationToken cancellationToken,
            HealthStatus? dependency = null,
            string environment = "Development")
        {
            WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
            {
                EnvironmentName = environment,
                ApplicationName = typeof(HealthEndpointTests).Assembly.GetName().Name
            });

            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.AddServiceDefaults();

            if (dependency is { } status)
            {
                builder.Services.AddHealthChecks()
                    .AddCheck("dependency", () => new HealthCheckResult(status));
            }

            WebApplication application = builder.Build();
            application.MapDefaultEndpoints();
            await application.StartAsync(cancellationToken);

            HttpClient client = new() { BaseAddress = new Uri(application.Urls.First()) };
            return new HealthProbeHost(application, client);
        }

        public async Task<HttpStatusCode> GetAsync(string path, CancellationToken cancellationToken)
        {
            using HttpResponseMessage response = await client.GetAsync(path, cancellationToken);
            return response.StatusCode;
        }

        public async ValueTask DisposeAsync()
        {
            client.Dispose();
            await application.DisposeAsync();
        }
    }
}
