namespace NetShield.Platform.Auditing;

/// <summary>
/// How a state-changing call ended, as the audit log records it.
/// </summary>
/// <remarks>
/// Stored as its name rather than its ordinal, so that adding a member cannot change what a row
/// written years earlier means. An append-only table has no way to be corrected.
/// </remarks>
public enum AuditOutcome
{
    /// <summary>The call did what it asked to do. <c>2xx</c>.</summary>
    Succeeded,

    /// <summary>The caller was refused by authentication or authorization. <c>401</c>, <c>403</c>.</summary>
    Denied,

    /// <summary>The call reached the handler and was rejected, or it failed. Every other code.</summary>
    Failed
}
