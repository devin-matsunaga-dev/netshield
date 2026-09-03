using Microsoft.AspNetCore.Http;

using NetShield.Platform.Problems;

namespace NetShield.Platform.Results;

/// <summary>
/// Turns a handler's <see cref="Result"/> into an HTTP response. This is the whole of the
/// endpoint layer's error handling: a handler decides what went wrong, this decides how to say
/// it, and neither knows about the other (CONVENTIONS.md §2 and §4).
/// </summary>
public static class ResultEndpointExtensions
{
    /// <summary>Maps a valueless result: <c>204</c> on success, problem details otherwise.</summary>
    public static IResult ToHttpResult(this Result result) =>
        result.IsSuccess ? TypedResults.NoContent() : new ProblemResult(result.Error);

    /// <summary>Maps a value-carrying result: <c>200</c> on success, problem details otherwise.</summary>
    public static IResult ToHttpResult<T>(this Result<T> result) =>
        result.IsSuccess ? TypedResults.Ok(result.Value) : new ProblemResult(result.Error);

    /// <summary>
    /// Maps a creating result: <c>201</c> with a <c>Location</c> header on success, problem
    /// details otherwise. CONVENTIONS.md §4 requires the header, so the caller supplies the URI
    /// rather than leaving it to be forgotten.
    /// </summary>
    public static IResult ToCreatedResult<T>(this Result<T> result, Func<T, string> location) =>
        result.IsSuccess
            ? TypedResults.Created(location(result.Value), result.Value)
            : new ProblemResult(result.Error);
}
