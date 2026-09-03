using FluentValidation;
using FluentValidation.Results;

using Microsoft.AspNetCore.Http;

using NetShield.Platform.Results;

namespace NetShield.Identity.Endpoints;

/// <summary>
/// Runs the registered <see cref="IValidator{T}"/> over the request body before the handler sees
/// it, so a handler may assume valid input (CONVENTIONS.md §4).
/// </summary>
/// <typeparam name="TRequest">The body shape to validate.</typeparam>
public sealed class ValidationFilter<TRequest>(IValidator<TRequest> validator) : IEndpointFilter
    where TRequest : class
{
    /// <summary>The code every shape rejection carries.</summary>
    public const string RejectionCode = "request.invalid";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (context.Arguments.OfType<TRequest>().FirstOrDefault() is not { } request)
        {
            return await next(context);
        }

        ValidationResult result = await validator.ValidateAsync(request, context.HttpContext.RequestAborted);

        if (result.IsValid)
        {
            return await next(context);
        }

        // Keyed by the JSON member name the client sent, camel-cased to match the wire shape.
        Dictionary<string, string[]> failures = result.Errors
            .GroupBy(failure => JsonMemberName(failure.PropertyName), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(failure => failure.ErrorMessage).ToArray(),
                StringComparer.Ordinal);

        return Result.Failure(
                Error.Validation(RejectionCode, "The request is not valid.", failures))
            .ToHttpResult();
    }

    private static string JsonMemberName(string propertyName) =>
        propertyName.Length == 0 ? propertyName : char.ToLowerInvariant(propertyName[0]) + propertyName[1..];
}
