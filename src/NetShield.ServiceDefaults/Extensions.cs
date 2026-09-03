using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Cross-cutting host wiring shared by every NetShield service: OpenTelemetry traces and
/// metrics, health checks, service discovery, and HTTP resilience (ARCHITECTURE.md §2,
/// CONVENTIONS.md §8). Referenced by Web.Host and Ingest.
/// </summary>
public static class Extensions
{
    /// <summary>Liveness. The process is up and responding; it says nothing about its dependencies.</summary>
    private const string LivenessEndpointPath = "/health";

    /// <summary>Readiness. Every registered check, including PostgreSQL and Redis, has to pass.</summary>
    private const string ReadinessEndpointPath = "/health/ready";

    /// <summary>Tag marking the checks that answer <see cref="LivenessEndpointPath"/>.</summary>
    private const string LivenessTag = "live";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();

        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(builder.Environment.ApplicationName)
                    // Health probes run on a timer and would otherwise dominate the trace stream.
                    .AddAspNetCoreInstrumentation(instrumentation =>
                        instrumentation.Filter = context =>
                            !context.Request.Path.StartsWithSegments(LivenessEndpointPath))
                    .AddHttpClientInstrumentation();
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), [LivenessTag]);

        return builder;
    }

    /// <summary>
    /// Maps the liveness and readiness endpoints. Health endpoints outside development have
    /// security implications and are enabled deliberately by the deployment package, not here.
    /// </summary>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            return app;
        }

        app.MapHealthChecks(LivenessEndpointPath, new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(LivenessTag)
        });

        app.MapHealthChecks(ReadinessEndpointPath);

        return app;
    }

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        bool useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }
}
