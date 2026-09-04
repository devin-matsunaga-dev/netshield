using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using NetShield.Inventory.Collector;
using NetShield.Inventory.Collector.Contract;
using NetShield.Inventory.Collector.Handlers;

using NetShield.Platform.Auditing;
using NetShield.Platform.Authentication;
using NetShield.Platform.Results;
using NetShield.Platform.Validation;

namespace NetShield.Inventory.Endpoints;

/// <summary>
/// The internal collector contract, under <c>/internal/collector</c> (ARCHITECTURE.md §7).
/// </summary>
/// <remarks>
/// <para>
/// Three routes, and they are not part of the API the SPA talks to. They are outside
/// <c>/api</c>, so they are absent from the OpenAPI document and from the generated TypeScript
/// client — deliberately, and checked by a test, because a leased job carries an opened device
/// credential and that shape must never appear in a contract a browser is built from.
/// </para>
/// <para>
/// They authenticate with the collector's shared secret and nothing else. A signed-in
/// administrator cannot reach them and a collector cannot reach <c>/api</c>: the two credentials
/// open disjoint sets of routes, which is what keeps "the decrypt path is reachable only from
/// the collector-job endpoint" true of the running system and not just of the code.
/// </para>
/// <para>
/// Auditing. The heartbeat and the result batch carry <c>[NoAudit]</c>: they are data-plane
/// traffic between two processes, arriving at the frequency of the poll interval and the size of
/// the estate, and a row for each would bury the rows that describe something a person did —
/// which is what WP-0.5 built the table for. What is worth recording is the security-relevant
/// act, and the lease writes it itself, one row per credential actually released. The lease is a
/// <c>GET</c> and so is outside the middleware's reach either way; the row is written by
/// <see cref="LeaseCollectorJobsHandler"/> for the same reason <c>--rewrap</c> writes its own.
/// </para>
/// </remarks>
public static class CollectorEndpoints
{
    /// <summary>The group every route below hangs from.</summary>
    public const string RoutePrefix = "/internal/collector";

    /// <summary>
    /// Maps the collector endpoints. Called by
    /// <see cref="InventoryEndpoints.MapInventoryEndpoints"/>, which is the module's single
    /// registration point (CONVENTIONS.md §2).
    /// </summary>
    public static IEndpointRouteBuilder MapCollectorEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints.MapGroup(RoutePrefix)
            .WithTags("Collector")

            // Excluded from the API description as well as from the document's /api filter, so
            // that a later change to how the document is filtered cannot quietly publish these.
            .ExcludeFromDescription()
            .RequireCollector();

        group.MapGet("/jobs", LeaseAsync)
            .WithName("LeaseCollectorJobs");

        group.MapPost("/results", SubmitAsync)
            .AddEndpointFilter<ValidationFilter<CollectorResultsRequest>>()
            .SkipAudit()
            .WithName("SubmitCollectorResults");

        group.MapPost("/heartbeat", HeartbeatAsync)
            .AddEndpointFilter<ValidationFilter<CollectorHeartbeatRequest>>()
            .SkipAudit()
            .WithName("RecordCollectorHeartbeat");

        return endpoints;
    }

    /// <summary>
    /// Leases a batch of due jobs.
    /// </summary>
    /// <remarks>
    /// A <c>GET</c> that changes state, which is unusual enough to say why: what the collector is
    /// doing is asking for its work, the lease is bookkeeping the API does in order to answer,
    /// and every other shape — a <c>POST</c> returning a list, a claim call followed by a fetch —
    /// makes the collector's loop harder to retry safely without making the API's job easier.
    /// ARCHITECTURE.md §7 names the route in these words.
    /// </remarks>
    private static async Task<IResult> LeaseAsync(
        HttpContext http,
        LeaseCollectorJobsHandler handler,
        CancellationToken cancellationToken,
        string? collector = null,
        int? limit = null)
    {
        // Optional in the signature and required by this check, so that a collector which forgot
        // to name itself gets the API's own problem shape rather than the framework's bare 400
        // (CONVENTIONS.md §4).
        if (string.IsNullOrWhiteSpace(collector))
        {
            return Result<CollectorJobBatch>.Failure(
                    Error.Validation(
                        "collector.name-required",
                        "The collector query parameter names which collector is asking for work."))
                .ToHttpResult();
        }

        CollectorCaller caller = new(collector.Trim(), http.Connection.RemoteIpAddress);

        return (await handler.HandleAsync(caller, limit, cancellationToken)).ToHttpResult();
    }

    private static async Task<IResult> SubmitAsync(
        CollectorResultsRequest request,
        SubmitCollectorResultsHandler handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(request, cancellationToken)).ToHttpResult();

    private static async Task<IResult> HeartbeatAsync(
        CollectorHeartbeatRequest request,
        RecordHeartbeatHandler handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(request, cancellationToken)).ToHttpResult();
}
