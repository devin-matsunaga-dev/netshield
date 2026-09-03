using System.Text.Json;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using NetShield.Platform.Problems;
using NetShield.Platform.Results;

namespace NetShield.UnitTests.Platform;

/// <summary>
/// A host wired the way <c>NetShield.Web.Host</c> is — problem details registered, the exception
/// handler first in the pipeline — with one endpoint per outcome the mapping has to produce.
/// A real server on an ephemeral loopback port; no container and no external dependency.
/// </summary>
internal sealed class ApiProbeHost(WebApplication application, HttpClient client) : IAsyncDisposable
{
    /// <summary>The secret an endpoint is made to fail on, to prove it never reaches the caller.</summary>
    public const string LeakedSecret = "hunter2";

    public static async Task<ApiProbeHost> StartAsync(CancellationToken cancellationToken, string environment = "Development")
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            EnvironmentName = environment,
            ApplicationName = typeof(ApiProbeHost).Assembly.GetName().Name
        });

        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddNetShieldProblemDetails();

        WebApplication application = builder.Build();
        application.UseNetShieldProblemDetails();

        application.MapGet("/ok", () => Result<Probe>.Success(new Probe("core-sw-01")).ToHttpResult());
        application.MapGet("/no-content", () => Result.Success.ToHttpResult());
        application.MapGet("/created", () => Result<Probe>.Success(new Probe("core-sw-01"))
            .ToCreatedResult(probe => $"/api/v1/devices/{probe.Hostname}"));

        application.MapGet("/validation", () => Result<Probe>
            .Failure(Error.Validation(
                "device.invalid",
                "The device is not valid.",
                new Dictionary<string, string[]> { ["hostname"] = ["Required."] }))
            .ToHttpResult());

        application.MapGet("/not-found", () => Result<Probe>
            .Failure(Error.NotFound("device.not-found", "No such device."))
            .ToHttpResult());

        application.MapGet("/forbidden", () => Result
            .Failure(Error.Forbidden("device.forbidden", "An Analyst may not do that."))
            .ToHttpResult());

        application.MapGet("/conflict", () => Result<Probe>
            .Failure(Error.Conflict("device.duplicate-ip", "That address is already in use."))
            .ToHttpResult());

        application.MapGet("/unprocessable", () => Result
            .Failure(Error.Unprocessable("device.unprocessable", "The device is decommissioned."))
            .ToHttpResult());

        application.MapGet("/rate-limited", () => Result
            .Failure(Error.RateLimited("device.rate-limited", "Slow down."))
            .ToHttpResult());

        application.MapGet("/boom", IResult () => throw new InvalidOperationException(
            $"npgsql: connection to netshield failed for user 'netshield' password={LeakedSecret}"));

        await application.StartAsync(cancellationToken);

        HttpClient client = new() { BaseAddress = new Uri(application.Urls.First()) };
        return new ApiProbeHost(application, client);
    }

    public async Task<ProbeResponse> GetAsync(string path, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await client.GetAsync(path, cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);

        return new ProbeResponse(
            (int)response.StatusCode,
            response.Content.Headers.ContentType?.MediaType,
            response.Headers.Location?.ToString(),
            body);
    }

    public async ValueTask DisposeAsync()
    {
        client.Dispose();
        await application.DisposeAsync();
    }

    /// <summary>The payload a successful probe endpoint returns.</summary>
    internal sealed record Probe(string Hostname);

    /// <summary>What a probe call produced, flattened so a test can assert on it directly.</summary>
    internal sealed record ProbeResponse(int Status, string? ContentType, string? Location, string Body)
    {
        public JsonElement Json => JsonDocument.Parse(Body).RootElement;

        public string? Member(string name) =>
            Json.TryGetProperty(name, out JsonElement value) ? value.ToString() : null;
    }
}
