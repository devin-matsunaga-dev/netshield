using System.Net;

namespace NetShield.Inventory.Collector.Contract;

/// <summary>
/// Who the API is talking to on the internal contract, as the request layer saw them.
/// </summary>
/// <remarks>
/// Passed from the endpoint into the handler rather than read out of an <c>HttpContext</c> the
/// handler reaches for, so that the audit row a lease writes can be produced — and tested —
/// without a request. The name is the collector's own claim; the address is the one fact about
/// the caller the API actually knows.
/// </remarks>
/// <param name="Name">What the caller says it is called.</param>
/// <param name="SourceIp">Where the request came from, for the audit row (SPEC.md §5).</param>
internal sealed record CollectorCaller(string Name, IPAddress? SourceIp);
