using Microsoft.AspNetCore.Http;

namespace NetShield.Platform.Problems;

/// <summary>
/// The <c>type</c> URI a problem response carries. RFC 9457 wants a stable, dereferenceable
/// identifier; the RFC 9110 status-code sections are exactly that and need nothing hosted.
/// </summary>
internal static class ProblemTypes
{
    private const string StatusCodeRegistry = "https://datatracker.ietf.org/doc/html/rfc9110#section-";

    internal static string ForStatus(int status) => status switch
    {
        StatusCodes.Status400BadRequest => $"{StatusCodeRegistry}15.5.1",
        StatusCodes.Status401Unauthorized => $"{StatusCodeRegistry}15.5.2",
        StatusCodes.Status403Forbidden => $"{StatusCodeRegistry}15.5.4",
        StatusCodes.Status404NotFound => $"{StatusCodeRegistry}15.5.5",
        StatusCodes.Status409Conflict => $"{StatusCodeRegistry}15.5.10",
        StatusCodes.Status422UnprocessableEntity => $"{StatusCodeRegistry}15.5.21",
        StatusCodes.Status429TooManyRequests => "https://datatracker.ietf.org/doc/html/rfc6585#section-4",
        StatusCodes.Status500InternalServerError => $"{StatusCodeRegistry}15.6.1",
        _ => "about:blank"
    };
}
