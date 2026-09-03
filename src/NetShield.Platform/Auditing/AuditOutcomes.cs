using Microsoft.AspNetCore.Http;

namespace NetShield.Platform.Auditing;

/// <summary>Classifies a response status code into an <see cref="AuditOutcome"/>.</summary>
internal static class AuditOutcomes
{
    /// <summary>
    /// A refusal is recorded apart from a rejection on purpose: "this account tried to do
    /// something it is not allowed to do" is the line an operator scans an audit log for, and it
    /// reads nothing like a validation failure.
    /// </summary>
    internal static AuditOutcome FromStatusCode(int statusCode) => statusCode switch
    {
        >= StatusCodes.Status200OK and < StatusCodes.Status400BadRequest => AuditOutcome.Succeeded,
        StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden => AuditOutcome.Denied,
        _ => AuditOutcome.Failed
    };
}
