namespace NetShield.Platform.Results;

/// <summary>
/// The categories of expected failure a handler may return. Each maps to exactly one status
/// code in CONVENTIONS.md §4, which is what lets the endpoint layer translate a
/// <see cref="Result"/> without knowing anything about the handler that produced it.
/// </summary>
/// <remarks>
/// There is deliberately no member for <c>500</c>. An unexpected failure is a bug or an
/// infrastructure fault, it is raised as an exception, and it is answered by
/// <c>NetShield.Platform.Problems.UnhandledExceptionHandler</c> — never returned as a result.
/// </remarks>
public enum ErrorKind
{
    /// <summary>The request could not be understood as written. <c>400</c>.</summary>
    Validation,

    /// <summary>The caller is not authenticated. <c>401</c>.</summary>
    Unauthenticated,

    /// <summary>The caller is authenticated but not permitted. <c>403</c>.</summary>
    Forbidden,

    /// <summary>The target does not exist, or the caller may not know that it does. <c>404</c>.</summary>
    NotFound,

    /// <summary>The request conflicts with the current state, such as a duplicate. <c>409</c>.</summary>
    Conflict,

    /// <summary>The request is well-formed but semantically rejected. <c>422</c>.</summary>
    Unprocessable,

    /// <summary>The caller has exceeded its rate limit. <c>429</c>.</summary>
    RateLimited
}
