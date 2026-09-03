using Microsoft.AspNetCore.Builder;

namespace NetShield.Platform.Auditing;

/// <summary>How an endpoint says what its audit row should be called.</summary>
public static class AuditEndpointExtensions
{
    /// <summary>
    /// Names the action recorded for this route, and optionally the kind of thing it acts on.
    /// </summary>
    public static TBuilder Audits<TBuilder>(this TBuilder builder, string action, string? targetType = null)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(action);

        builder.WithMetadata(new AuditActionMetadata(action, targetType));

        return builder;
    }

    /// <summary>Keeps this route out of the audit log. See <see cref="NoAuditAttribute"/>.</summary>
    public static TBuilder SkipAudit<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.WithMetadata(new NoAuditAttribute());

        return builder;
    }
}
