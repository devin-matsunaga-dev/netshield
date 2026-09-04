using System.Text.Json;

using NetShield.Contracts.Collector;

namespace NetShield.Inventory.Collector.Contract;

/// <summary>
/// One job, leased: everything the collector needs to run it and nothing else.
/// </summary>
/// <param name="JobId">The job.</param>
/// <param name="Kind">What to do.</param>
/// <param name="LeaseToken">
/// The token this lease generation is identified by. The collector sends it back with the
/// result, and a result under any other token is refused — which is what stops a slow collector
/// whose lease expired from overwriting the result of the one that picked the job up next.
/// </param>
/// <param name="LeaseExpiresAt">
/// When the API will consider this lease abandoned and let another collector claim the job. UTC.
/// A collector that is still working past it should stop rather than submit.
/// </param>
/// <param name="Attempt">Which attempt this is, so the collector can log a retry as one.</param>
/// <param name="Device">The device to talk to, when the job is about a device.</param>
/// <param name="Parameters">Kind-specific arguments, opaque in WP-1.3.</param>
/// <param name="Credential">
/// The credential to authenticate with, when the job named a profile. Absent when the job needs
/// none — an ICMP probe does not authenticate to anything.
/// </param>
internal sealed record CollectorJobLease(
    Guid JobId,
    CollectorJobKind Kind,
    string LeaseToken,
    DateTimeOffset LeaseExpiresAt,
    int Attempt,
    CollectorJobDevice? Device,
    JsonElement? Parameters,
    CollectorJobCredential? Credential);
