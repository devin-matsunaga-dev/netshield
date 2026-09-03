using System.Diagnostics;
using System.Globalization;
using System.Security.Claims;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using NetShield.Platform.Authorization;
using NetShield.Platform.Logging;
using NetShield.Platform.Time;

namespace NetShield.Platform.Auditing;

/// <summary>
/// Writes one <c>audit_log</c> row for every state-changing call, without the endpoint having to
/// ask (SPEC.md §5).
/// </summary>
/// <remarks>
/// <para>
/// It records after the call rather than inside it. That is what lets a row exist for a request
/// the endpoint never saw — a 401 from the authentication pipeline, a 403 from authorization —
/// which is the half of an audit log that matters most when something has gone wrong. The cost
/// is that the row is written in its own transaction, after the domain change has committed: a
/// process that dies in the window between the two loses the row. That trade is deliberate, and
/// the answer if it ever stops being acceptable is to route audit through the transactional
/// outbox, not to scatter a hand-written append into every handler.
/// </para>
/// <para>
/// Reads are not audited. At the scale in SPEC.md §1 a row per query would bury the rows that
/// describe a change, and SPEC.md §5 asks for the state-changing calls.
/// </para>
/// </remarks>
internal sealed class AuditMiddleware(
    RequestDelegate next,
    IServiceScopeFactory scopes,
    SecretRedactor redactor,
    IClock clock,
    ILogger<AuditMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, AuditContext audit)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(audit);

        try
        {
            await next(context);
        }
        catch
        {
            // The exception handler upstream turns this into a 500. The row is written first,
            // because a state-changing call that threw is exactly the one worth having recorded.
            await RecordAsync(context, audit, StatusCodes.Status500InternalServerError);
            throw;
        }

        await RecordAsync(context, audit, context.Response.StatusCode);
    }

    /// <summary>
    /// A call is auditable when it was routed somewhere, it changes state, and the route did not
    /// opt out. An unrouted request is left alone deliberately: a 404 for a path nothing serves
    /// is a scanner, not an act.
    /// </summary>
    private static bool ShouldAudit(HttpContext context)
    {
        if (!IsStateChanging(context.Request.Method))
        {
            return false;
        }

        Endpoint? endpoint = context.GetEndpoint();

        return endpoint is not null && endpoint.Metadata.GetMetadata<NoAuditAttribute>() is null;
    }

    private static bool IsStateChanging(string method) =>
        HttpMethods.IsPost(method)
        || HttpMethods.IsPut(method)
        || HttpMethods.IsPatch(method)
        || HttpMethods.IsDelete(method);

    private async Task RecordAsync(HttpContext context, AuditContext audit, int statusCode)
    {
        if (!ShouldAudit(context))
        {
            return;
        }

        try
        {
            AuditEntry entry = Build(context, audit, statusCode);

            // Its own scope, so the write cannot pick up whatever a handler left tracked on the
            // request's own context and save that too.
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();

            await scope.ServiceProvider.GetRequiredService<IAuditLog>()
                .AppendAsync(entry, CancellationToken.None);
        }
        catch (Exception exception)
        {
            // The response has already been decided; there is nothing useful to tell the caller.
            // An operator, on the other hand, needs to know the audit log has a hole in it.
            logger.LogError(
                exception,
                "Failed to write the audit row for {Method} {Path}, which answered {StatusCode}.",
                context.Request.Method,
                context.Request.Path.Value,
                statusCode);
        }
    }

    private AuditEntry Build(HttpContext context, AuditContext audit, int statusCode)
    {
        ClaimsPrincipal principal = context.User;

        Guid? actorId = audit.ActorUserId
            ?? (Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out Guid id) ? id : null);

        return new AuditEntry
        {
            Id = Guid.CreateVersion7(),
            CreatedAt = clock.UtcNow,
            ActorUserId = actorId,
            ActorUsername = audit.ActorUsername ?? principal.FindFirstValue(ClaimTypes.Name),
            ActorRole = audit.ActorRole ?? AuthorizationClaims.RoleOf(principal),
            SourceIp = context.Connection.RemoteIpAddress?.ToString(),
            Action = audit.ActionName ?? ActionFor(context),
            TargetType = audit.TargetType ?? TargetTypeFor(context),
            TargetId = audit.TargetId,
            Outcome = AuditOutcomes.FromStatusCode(statusCode),
            Before = AuditPayload.Serialize(audit.BeforeState, redactor),
            After = AuditPayload.Serialize(audit.AfterState, redactor),
            HttpMethod = context.Request.Method,
            Path = context.Request.Path.Value ?? "/",
            StatusCode = statusCode,
            TraceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier
        };
    }

    /// <summary>
    /// The route's declared action, or the method and route pattern when it declared none — so a
    /// new endpoint is audited from the moment it is mapped, whether or not anyone remembered to
    /// name it.
    /// </summary>
    private static string ActionFor(HttpContext context)
    {
        Endpoint? endpoint = context.GetEndpoint();

        if (endpoint?.Metadata.GetMetadata<AuditActionMetadata>() is { } metadata)
        {
            return metadata.Action;
        }

        string route = (endpoint as RouteEndpoint)?.RoutePattern.RawText ?? context.Request.Path.Value ?? "/";

        return string.Create(CultureInfo.InvariantCulture, $"{context.Request.Method} {route}");
    }

    private static string? TargetTypeFor(HttpContext context) =>
        context.GetEndpoint()?.Metadata.GetMetadata<AuditActionMetadata>()?.TargetType;
}
