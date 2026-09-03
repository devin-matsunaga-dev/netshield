using NetShield.Contracts.Identity;

namespace NetShield.Platform.Auditing;

/// <summary>
/// One row of the append-only audit log: who did what, to what, from where, and how it ended.
/// </summary>
/// <remarks>
/// <para>
/// Every property is <c>init</c>-only. There is no setter to call, no <c>DbSet</c> to reach the
/// table through, and no method anywhere in <c>NetShield.Platform.Auditing</c> that updates or
/// removes a row — enforced by <c>NetShield.ArchitectureTests</c>, and again by a trigger in the
/// database (ARCHITECTURE.md §8).
/// </para>
/// <para>
/// There is deliberately no <c>updated_at</c>, which CONVENTIONS.md §3 otherwise asks of every
/// table. A column that can never change on a table that can never be updated would be a
/// statement the schema cannot keep.
/// </para>
/// </remarks>
public sealed class AuditEntry
{
    /// <summary>UUID v7, so the primary key is also the order the events happened in.</summary>
    public Guid Id { get; init; }

    /// <summary>When the call was recorded. UTC.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// The account that made the call, or <see langword="null"/> when the caller was anonymous —
    /// a refused request and a failed sign-in are both worth a row.
    /// </summary>
    public Guid? ActorUserId { get; init; }

    /// <summary>The actor's account name as the session carried it.</summary>
    public string? ActorUsername { get; init; }

    /// <summary>
    /// The role the session held at the time. Kept alongside the id because a role that changes
    /// later must not rewrite what this row says about the past.
    /// </summary>
    public UserRole? ActorRole { get; init; }

    /// <summary>The address the request arrived from (SPEC.md §5).</summary>
    public string? SourceIp { get; init; }

    /// <summary>What was done, as a stable dotted identifier — <c>identity.login</c>.</summary>
    public required string Action { get; init; }

    /// <summary>What kind of thing was acted on — <c>user</c>, <c>device</c>.</summary>
    public string? TargetType { get; init; }

    /// <summary>Which one, when the call named one.</summary>
    public string? TargetId { get; init; }

    /// <summary>How the call ended.</summary>
    public AuditOutcome Outcome { get; init; }

    /// <summary>
    /// The state before the change, as redacted JSON, or <see langword="null"/> when the call had
    /// no before-state to describe.
    /// </summary>
    public string? Before { get; init; }

    /// <summary>The state after the change, as redacted JSON.</summary>
    public string? After { get; init; }

    /// <summary>The request method.</summary>
    public required string HttpMethod { get; init; }

    /// <summary>The request path, without its query string.</summary>
    public required string Path { get; init; }

    /// <summary>The status code the call answered with.</summary>
    public int StatusCode { get; init; }

    /// <summary>The correlation id, so a row joins the trace it came from (CONVENTIONS.md §8).</summary>
    public string? TraceId { get; init; }
}
