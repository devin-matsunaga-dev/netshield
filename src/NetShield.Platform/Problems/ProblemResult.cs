using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Microsoft.Extensions.DependencyInjection;

using NetShield.Platform.Results;

namespace NetShield.Platform.Problems;

/// <summary>
/// Writes an <see cref="Error"/> as RFC 9457 <c>application/problem+json</c>. It goes through
/// <see cref="IProblemDetailsService"/> so that a handled failure and an unhandled exception
/// produce the identical response shape, <c>traceId</c> included.
/// </summary>
internal sealed class ProblemResult(Error error) : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        int status = error.Kind.ToStatusCode();

        ProblemDetails details = new()
        {
            Status = status,
            Title = error.Kind.ToTitle(),
            Detail = error.Message,
            Type = ProblemTypes.ForStatus(status)
        };

        details.Extensions["code"] = error.Code;

        if (error.Failures is { Count: > 0 } failures)
        {
            details.Extensions["errors"] = failures;
        }

        httpContext.Response.StatusCode = status;

        IProblemDetailsService? service = httpContext.RequestServices.GetService<IProblemDetailsService>();

        if (service is not null && await service.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = details
        }))
        {
            return;
        }

        // Only reached when AddNetShieldProblemDetails was not called. The response still has to
        // carry a trace id, so the enrichment is applied here rather than assumed.
        ProblemDetailsExtensions.Enrich(details, httpContext);
        await httpContext.Response.WriteAsJsonAsync(
            details,
            options: null,
            contentType: "application/problem+json",
            httpContext.RequestAborted);
    }
}
