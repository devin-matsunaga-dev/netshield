namespace NetShield.Platform.Auditing;

/// <summary>
/// The only way a row reaches <c>audit_log</c>.
/// </summary>
/// <remarks>
/// One member, and it appends. There is no counterpart that changes a row or takes one away, in
/// this interface or anywhere else in the system, and there never may be — ARCHITECTURE.md §8 and
/// CLAUDE.md both put it in those words. A test in <c>NetShield.ArchitectureTests</c> fails the
/// build if one appears, and a trigger in the database refuses it even then.
/// </remarks>
public interface IAuditLog
{
    /// <summary>Writes <paramref name="entry"/>. It cannot afterwards be altered or withdrawn.</summary>
    Task AppendAsync(AuditEntry entry, CancellationToken cancellationToken);
}
