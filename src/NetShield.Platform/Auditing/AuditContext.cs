using NetShield.Contracts.Identity;

namespace NetShield.Platform.Auditing;

/// <summary>
/// The per-request collector behind <see cref="IAuditContext"/>. Holds what handlers said until
/// the middleware writes the row.
/// </summary>
/// <remarks>
/// Scoped, so it lives exactly as long as the request it describes. It records and never writes:
/// a handler that enriches the audit row must not be able to decide when — or whether — the row
/// is persisted.
/// </remarks>
internal sealed class AuditContext : IAuditContext
{
    internal Guid? ActorUserId { get; private set; }

    internal string? ActorUsername { get; private set; }

    internal UserRole? ActorRole { get; private set; }

    internal string? ActionName { get; private set; }

    internal string? TargetType { get; private set; }

    internal string? TargetId { get; private set; }

    internal IReadOnlyDictionary<string, object?>? BeforeState { get; private set; }

    internal IReadOnlyDictionary<string, object?>? AfterState { get; private set; }

    public void Actor(Guid userId, string username, UserRole role)
    {
        ArgumentException.ThrowIfNullOrEmpty(username);

        ActorUserId = userId;
        ActorUsername = username;
        ActorRole = role;
    }

    public void Action(string action)
    {
        ArgumentException.ThrowIfNullOrEmpty(action);

        ActionName = action;
    }

    public void Target(string targetType, string? targetId = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(targetType);

        TargetType = targetType;
        TargetId = targetId;
    }

    public void Snapshot(
        IReadOnlyDictionary<string, object?>? before,
        IReadOnlyDictionary<string, object?>? after)
    {
        BeforeState = before;
        AfterState = after;
    }
}
