using NetShield.Contracts.Identity;

namespace NetShield.Platform.Auditing;

/// <summary>
/// What a handler adds to the audit row the middleware is going to write for this request.
/// </summary>
/// <remarks>
/// <para>
/// The row is written whether or not a handler says anything: actor, source address, action,
/// outcome and status come from the request itself, which is what makes the recording automatic
/// rather than something each handler has to remember. What the request layer cannot know is the
/// domain detail — which thing was acted on, and what changed about it — so a handler supplies
/// that here.
/// </para>
/// <para>
/// Nothing put here is trusted to be safe: every snapshot value is redacted by property name
/// before it reaches the database (SPEC.md §5).
/// </para>
/// </remarks>
public interface IAuditContext
{
    /// <summary>
    /// Names the actor when the request could not. A sign-in arrives anonymous and ends up
    /// belonging to somebody; this is how the row says who.
    /// </summary>
    void Actor(Guid userId, string username, UserRole role);

    /// <summary>
    /// Overrides the action recorded for this request, when one route can do more than one thing.
    /// </summary>
    void Action(string action);

    /// <summary>Names what was acted on.</summary>
    /// <param name="targetType">The kind of thing — <c>user</c>, <c>device</c>.</param>
    /// <param name="targetId">Which one, when the call names one.</param>
    void Target(string targetType, string? targetId = null);

    /// <summary>
    /// Records what changed. Either side may be <see langword="null"/> — a creation has no
    /// before, a deletion has no after.
    /// </summary>
    void Snapshot(
        IReadOnlyDictionary<string, object?>? before,
        IReadOnlyDictionary<string, object?>? after);
}
