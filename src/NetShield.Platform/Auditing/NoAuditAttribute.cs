namespace NetShield.Platform.Auditing;

/// <summary>
/// Endpoint metadata keeping a state-changing route out of the audit log.
/// </summary>
/// <remarks>
/// Reserved for a route whose traffic is machine chatter rather than an act by a person — the
/// collector's result and heartbeat posts in WP-1.3 are the case this exists for. Anything a
/// user can do stays audited; SPEC.md §5 does not offer an exemption.
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class NoAuditAttribute : Attribute;
