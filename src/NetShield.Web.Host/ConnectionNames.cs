namespace NetShield.Web.Host;

/// <summary>
/// The Aspire connection names this host resolves. Each one is declared by a resource in
/// <c>NetShield.AppHost</c>; the value behind it is supplied at run time and never
/// appears in configuration checked into the repository.
/// </summary>
internal static class ConnectionNames
{
    /// <summary>PostgreSQL 17 with TimescaleDB — the single NetShield store.</summary>
    internal const string Database = "netshield";

    /// <summary>Redis — cache, rate limiting, job coordination, SignalR backplane.</summary>
    internal const string Cache = "cache";

    /// <summary>The SMTP sink used by notification channels from Phase 6 onwards.</summary>
    internal const string Mail = "mail";
}
