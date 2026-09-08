using System.Net;

using NetShield.Contracts.Inventory;

namespace NetShield.Inventory.Resolution;

/// <summary>
/// <c>ResolveAssetAt(ip, timestamp)</c> — what held an address at an instant.
/// </summary>
/// <remarks>
/// <para>
/// The service every downstream enrichment leans on. ARCHITECTURE.md §6 puts "asset + client
/// resolution at event timestamp" in the ingest pipeline between the parser and the batch writer,
/// so every Phase 4 flow record and every Phase 5 log event is attributed through this. A
/// resolution that is wrong at a handover boundary is wrong silently, in data nobody re-reads,
/// which is why the interval table behind it carries its invariants in the database rather than
/// in the handler that maintains them.
/// </para>
/// <para>
/// <strong>Internal to the module</strong>, in the shape the credential resolver has been in
/// since WP-1.2: a module may not reference another module (ARCHITECTURE.md §4), and how
/// <c>NetShield.Ingest</c> — a separate process — eventually reaches this is a decision for the
/// package that builds the ingest path. Its read surface today is
/// <c>GET /api/v1/clients/resolve</c>, which is how an operator checks an attribution and how the
/// handover criterion is verified by hand.
/// </para>
/// <para>
/// <strong>Never throws for an address it does not know.</strong> An address outside the estate
/// is the ordinary case, and ARCHITECTURE.md §6 requires enrichment to land an event with a null
/// asset reference rather than to fail the write — so <c>Unresolved</c> is an answer.
/// </para>
/// </remarks>
internal interface IAssetResolver
{
    /// <summary>
    /// What held <paramref name="address"/> at <paramref name="at"/>.
    /// </summary>
    /// <param name="address">The address to resolve.</param>
    /// <param name="at">The instant to resolve it at. UTC.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task<AssetResolution> ResolveAtAsync(
        IPAddress address,
        DateTimeOffset at,
        CancellationToken cancellationToken);
}
