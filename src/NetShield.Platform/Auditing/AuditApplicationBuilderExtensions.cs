using Microsoft.AspNetCore.Builder;

namespace NetShield.Platform.Auditing;

/// <summary>Puts the audit recorder into the request pipeline.</summary>
public static class AuditApplicationBuilderExtensions
{
    /// <summary>
    /// Records every state-changing call. Register it after <c>UseAuthentication</c> and
    /// <c>UseRouting</c> and before the endpoints, so that the row knows who the caller was and
    /// what they were routed to — including when authorization refused them.
    /// </summary>
    public static IApplicationBuilder UseNetShieldAudit(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseMiddleware<AuditMiddleware>();
    }
}
