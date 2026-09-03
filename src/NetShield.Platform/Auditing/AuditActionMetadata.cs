namespace NetShield.Platform.Auditing;

/// <summary>
/// Endpoint metadata naming the action an audit row records for a route.
/// </summary>
/// <remarks>
/// Without it a row still gets written, named after the method and route pattern. The metadata
/// exists so that a row reads as a business event — <c>identity.login</c> — rather than as an
/// HTTP fact, which is what CONVENTIONS.md §8 asks of anything at <c>Information</c> and above.
/// </remarks>
/// <param name="Action">The stable dotted identifier for what the route does.</param>
/// <param name="TargetType">The kind of thing it acts on, when the route always acts on one.</param>
public sealed record AuditActionMetadata(string Action, string? TargetType = null);
