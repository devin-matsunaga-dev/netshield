namespace NetShield.Platform.Results;

/// <summary>
/// An expected failure, described well enough to answer the caller without an exception
/// (CONVENTIONS.md §2). Nothing here reaches the wire unfiltered: the endpoint layer decides
/// what to publish, and SPEC.md §5 forbids a credential, a SQL fragment or a stack trace in
/// any of these fields.
/// </summary>
/// <param name="Kind">The category, which fixes the status code.</param>
/// <param name="Code">
/// A stable machine-readable identifier such as <c>device.duplicate-ip</c>. Clients branch on
/// this; they never parse <paramref name="Message"/>.
/// </param>
/// <param name="Message">A human-readable sentence safe to show a user.</param>
public sealed record Error(ErrorKind Kind, string Code, string Message)
{
    /// <summary>
    /// Per-field validation failures, keyed by the field name in the request shape. Rendered
    /// as the <c>errors</c> member of the problem-details response.
    /// </summary>
    public IReadOnlyDictionary<string, string[]>? Failures { get; init; }

    /// <summary>The request could not be understood as written. <c>400</c>.</summary>
    public static Error Validation(string code, string message) =>
        new(ErrorKind.Validation, code, message);

    /// <summary>The request could not be understood as written, with per-field detail. <c>400</c>.</summary>
    public static Error Validation(string code, string message, IReadOnlyDictionary<string, string[]> failures) =>
        new(ErrorKind.Validation, code, message) { Failures = failures };

    /// <summary>The caller is not authenticated. <c>401</c>.</summary>
    public static Error Unauthenticated(string code, string message) =>
        new(ErrorKind.Unauthenticated, code, message);

    /// <summary>The caller is authenticated but not permitted. <c>403</c>.</summary>
    public static Error Forbidden(string code, string message) =>
        new(ErrorKind.Forbidden, code, message);

    /// <summary>The target does not exist, or the caller may not know that it does. <c>404</c>.</summary>
    public static Error NotFound(string code, string message) =>
        new(ErrorKind.NotFound, code, message);

    /// <summary>The request conflicts with the current state. <c>409</c>.</summary>
    public static Error Conflict(string code, string message) =>
        new(ErrorKind.Conflict, code, message);

    /// <summary>The request is well-formed but semantically rejected. <c>422</c>.</summary>
    public static Error Unprocessable(string code, string message) =>
        new(ErrorKind.Unprocessable, code, message);

    /// <summary>The caller has exceeded its rate limit. <c>429</c>.</summary>
    public static Error RateLimited(string code, string message) =>
        new(ErrorKind.RateLimited, code, message);
}
