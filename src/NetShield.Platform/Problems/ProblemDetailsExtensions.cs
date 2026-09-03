using System.Diagnostics;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Microsoft.Extensions.DependencyInjection;

namespace NetShield.Platform.Problems;

/// <summary>
/// Registers the single error shape every NetShield endpoint answers with: RFC 9457
/// <c>application/problem+json</c> carrying a <c>traceId</c>, and never an exception message,
/// a stack trace, a SQL fragment or a credential (CONVENTIONS.md §4, SPEC.md §5).
/// </summary>
public static class ProblemDetailsExtensions
{
    /// <summary>
    /// Adds problem details and the handler that answers an unhandled exception. Call
    /// <see cref="UseNetShieldProblemDetails"/> in the pipeline to activate the handler.
    /// </summary>
    public static IServiceCollection AddNetShieldProblemDetails(this IServiceCollection services)
    {
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
            Enrich(context.ProblemDetails, context.HttpContext));

        services.AddExceptionHandler<UnhandledExceptionHandler>();

        return services;
    }

    /// <summary>
    /// Puts the exception handler at the front of the pipeline. Registered for every
    /// environment on purpose: the developer exception page renders a stack trace, and
    /// SPEC.md §5 does not carve out Development.
    /// </summary>
    public static IApplicationBuilder UseNetShieldProblemDetails(this IApplicationBuilder app) =>
        app.UseExceptionHandler();

    /// <summary>
    /// Applies the members every NetShield problem response carries, whoever wrote it. The
    /// trace id is the W3C id the rest of the telemetry is keyed by, so a user quoting it from
    /// an error message lands on the exact request in the dashboard (CONVENTIONS.md §8).
    /// </summary>
    internal static void Enrich(ProblemDetails details, HttpContext httpContext)
    {
        details.Status ??= httpContext.Response.StatusCode;
        details.Type ??= ProblemTypes.ForStatus(details.Status.Value);
        details.Instance ??= $"{httpContext.Request.Method} {httpContext.Request.Path}";
        details.Extensions["traceId"] = Activity.Current?.TraceId.ToString() ?? httpContext.TraceIdentifier;
    }
}
