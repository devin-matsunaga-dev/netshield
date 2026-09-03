using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Microsoft.Extensions.Logging;

namespace NetShield.Platform.Problems;

/// <summary>
/// Answers an unhandled exception with problem details. An exception means a bug or an
/// infrastructure failure (CONVENTIONS.md §2), so the caller is told only that the request
/// failed and which trace to quote; the exception itself goes to the log, where it belongs.
/// </summary>
internal sealed class UnhandledExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<UnhandledExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        logger.LogError(
            exception,
            "Unhandled exception answering {RequestMethod} {RequestPath}",
            httpContext.Request.Method,
            httpContext.Request.Path);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        // The exception is deliberately not placed on the ProblemDetailsContext. Anything put
        // there is reachable by a customization and could be serialised; the response must
        // carry no message, no type name and no stack trace (SPEC.md §5).
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred.",
                Detail = "The request could not be completed. Quote the trace id when reporting this.",
                Type = ProblemTypes.ForStatus(StatusCodes.Status500InternalServerError)
            }
        });
    }
}
