using Microsoft.AspNetCore.Http;

namespace NetShield.Platform.Results;

/// <summary>
/// The one place an <see cref="ErrorKind"/> becomes a status code. CONVENTIONS.md §4 fixes
/// this table; a handler never chooses a status code for itself.
/// </summary>
public static class ErrorKindExtensions
{
    /// <summary>The HTTP status code that answers <paramref name="kind"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A new member was added to <see cref="ErrorKind"/> without a status code. That is a bug in
    /// this table, not a runtime condition.
    /// </exception>
    public static int ToStatusCode(this ErrorKind kind) => kind switch
    {
        ErrorKind.Validation => StatusCodes.Status400BadRequest,
        ErrorKind.Unauthenticated => StatusCodes.Status401Unauthorized,
        ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
        ErrorKind.NotFound => StatusCodes.Status404NotFound,
        ErrorKind.Conflict => StatusCodes.Status409Conflict,
        ErrorKind.Unprocessable => StatusCodes.Status422UnprocessableEntity,
        ErrorKind.RateLimited => StatusCodes.Status429TooManyRequests,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "No status code is mapped for this error kind.")
    };

    /// <summary>The problem-details title for <paramref name="kind"/>. Never carries detail.</summary>
    public static string ToTitle(this ErrorKind kind) => kind switch
    {
        ErrorKind.Validation => "The request is not valid.",
        ErrorKind.Unauthenticated => "Authentication is required.",
        ErrorKind.Forbidden => "You do not have permission to do that.",
        ErrorKind.NotFound => "The requested resource was not found.",
        ErrorKind.Conflict => "The request conflicts with the current state.",
        ErrorKind.Unprocessable => "The request could not be processed.",
        ErrorKind.RateLimited => "Too many requests.",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "No title is mapped for this error kind.")
    };
}
