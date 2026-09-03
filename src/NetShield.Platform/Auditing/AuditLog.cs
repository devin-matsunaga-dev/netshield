using NetShield.Platform.Persistence;

namespace NetShield.Platform.Auditing;

/// <summary>
/// Appends to <c>audit_log</c> through <see cref="PlatformDbContext"/>.
/// </summary>
/// <remarks>
/// The table is reached with <c>Set&lt;AuditEntry&gt;()</c> rather than through a <c>DbSet</c>
/// property on the context, so that nothing in the solution has a handle on which
/// <c>Remove</c> or <c>ExecuteDelete</c> could be called.
/// </remarks>
internal sealed class AuditLog(PlatformDbContext database) : IAuditLog
{
    public async Task AppendAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        database.Set<AuditEntry>().Add(entry);

        await database.SaveChangesAsync(cancellationToken);
    }
}
