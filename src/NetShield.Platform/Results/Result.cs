using System.Diagnostics.CodeAnalysis;

namespace NetShield.Platform.Results;

/// <summary>
/// The outcome of an operation that returns no value. CONVENTIONS.md §2: handlers return a
/// result and the endpoint layer maps it to a status code; an exception means a bug or an
/// infrastructure failure, never an expected outcome.
/// </summary>
public sealed record Result
{
    private Result(Error? error) => Error = error;

    /// <summary>The failure, or <see langword="null"/> when the operation succeeded.</summary>
    public Error? Error { get; }

    /// <summary>Whether the operation succeeded.</summary>
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;

    /// <summary>The single success value. Carrying no payload, it needs no allocation per call.</summary>
    public static Result Success { get; } = new(error: null);

    /// <summary>An unsuccessful outcome.</summary>
    public static Result Failure(Error error) => new(error);

    /// <summary>Lets a handler <c>return someError;</c> without naming <see cref="Failure"/>.</summary>
    public static implicit operator Result(Error error) => new(error);
}
