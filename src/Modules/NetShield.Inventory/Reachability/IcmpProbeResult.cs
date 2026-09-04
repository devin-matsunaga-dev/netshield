using System.Text.Json.Serialization;

namespace NetShield.Inventory.Reachability;

/// <summary>
/// What the collector found, as it is written into <c>collector_jobs.result</c>.
/// </summary>
/// <remarks>
/// <para>
/// This shape is only ever produced by a probe that <em>ran</em>. A probe the collector could not
/// perform at all reports a failed job with a redacted sentence and no payload, and the result
/// handler records that against the device without letting it touch the device's state — the
/// health of the collector is not evidence about the estate.
/// </para>
/// <para>
/// <see cref="Replies"/> is the "RTT recorded per probe" this package owes: one entry per echo
/// request sent, in the order they were sent, carrying the round trip or nothing if that request
/// went unanswered. The summary members above it are derived from the answered ones and are what
/// the reachability row keeps; the per-request detail stays on the job row, which is where the
/// full record of one unit of work belongs until <c>metric_samples</c> exists to hold a series
/// (WP-3.1).
/// </para>
/// </remarks>
/// <param name="Probe">The discriminator, matching <see cref="IcmpProbeParameters.ProbeName"/>.</param>
/// <param name="Address">The address that was probed, as the collector was given it.</param>
/// <param name="Sent">How many echo requests went out.</param>
/// <param name="Received">How many replies came back.</param>
/// <param name="LossPercent">The proportion that did not, 0 to 100.</param>
/// <param name="RttMillisecondsMin">The fastest reply, or nothing if there were none.</param>
/// <param name="RttMillisecondsAvg">The mean of the replies, or nothing if there were none.</param>
/// <param name="RttMillisecondsMax">The slowest reply, or nothing if there were none.</param>
/// <param name="Replies">One entry per request sent.</param>
internal sealed record IcmpProbeResult(
    [property: JsonPropertyName("probe")] string? Probe,
    [property: JsonPropertyName("address")] string? Address,
    [property: JsonPropertyName("sent")] int Sent,
    [property: JsonPropertyName("received")] int Received,
    [property: JsonPropertyName("lossPercent")] double LossPercent,
    [property: JsonPropertyName("rttMillisecondsMin")] double? RttMillisecondsMin,
    [property: JsonPropertyName("rttMillisecondsAvg")] double? RttMillisecondsAvg,
    [property: JsonPropertyName("rttMillisecondsMax")] double? RttMillisecondsMax,
    [property: JsonPropertyName("replies")] IReadOnlyList<IcmpProbeReply>? Replies);

/// <summary>One echo request and what came back for it.</summary>
/// <param name="Sequence">Which request this was, from zero.</param>
/// <param name="RttMilliseconds">
/// The round trip, or nothing if no reply arrived before the probe's deadline.
/// </param>
internal sealed record IcmpProbeReply(
    [property: JsonPropertyName("sequence")] int Sequence,
    [property: JsonPropertyName("rttMilliseconds")] double? RttMilliseconds);
