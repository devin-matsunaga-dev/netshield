using System.Diagnostics.CodeAnalysis;

namespace NetShield.Platform.Results;

/// <summary>
/// The outcome of an operation that returns a value on success. See <see cref="Result"/> for
/// the rule this exists to serve.
/// </summary>
/// <typeparam name="T">The value produced on success. A DTO from <c>NetShield.Contracts</c>.</typeparam>
public sealed record Result<T>
{
    private readonly T? _value;

    private Result(T? value, Error? error)
    {
        _value = value;
        Error = error;
    }

    /// <summary>The failure, or <see langword="null"/> when the operation succeeded.</summary>
    public Error? Error { get; }

    /// <summary>Whether the operation succeeded.</summary>
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;

    /// <summary>
    /// The produced value. Reading it on a failed result is a programming error, not a
    /// runtime condition to branch on — check <see cref="IsSuccess"/> first.
    /// </summary>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException(
            $"Result<{typeof(T).Name}> failed with '{Error.Code}' and carries no value.");

    /// <summary>A successful outcome carrying <paramref name="value"/>.</summary>
    public static Result<T> Success(T value) => new(value, error: null);

    /// <summary>An unsuccessful outcome.</summary>
    public static Result<T> Failure(Error error) => new(default, error);

    /// <summary>Lets a handler <c>return dto;</c> without naming <see cref="Success"/>.</summary>
    public static implicit operator Result<T>(T value) => new(value, error: null);

    /// <summary>Lets a handler <c>return someError;</c> without naming <see cref="Failure"/>.</summary>
    public static implicit operator Result<T>(Error error) => new(default, error);
}
