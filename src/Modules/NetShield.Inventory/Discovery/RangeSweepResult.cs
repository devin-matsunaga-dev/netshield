using System.Text.Json.Serialization;

namespace NetShield.Inventory.Discovery;

/// <summary>
/// What a range sweep found, as it is written into <c>collector_jobs.result</c>.
/// </summary>
/// <remarks>
/// <para>
/// Only responders are carried. A span of 256 addresses where two answer is two entries, not 256
/// with 254 of them saying nothing happened — the same shape the table that stores it takes, and
/// for the same reason.
/// </para>
/// <para>
/// A sweep that ran and heard nothing at all is a <em>successful</em> job with an empty list,
/// the same distinction WP-1.4 drew for a probe: a hundred per cent silence is evidence about
/// the range, while a collector that could not open an ICMP socket is a failure that must not be
/// read as an empty estate.
/// </para>
/// </remarks>
/// <param name="Walk">The discriminator, matching <see cref="RangeSweepParameters.WalkName"/>.</param>
/// <param name="FirstAddress">The first address of the span, echoed back.</param>
/// <param name="LastAddress">The last address of the span, echoed back.</param>
/// <param name="Scanned">How many addresses were actually probed, after exclusions.</param>
/// <param name="Excluded">How many of the span's addresses the exclusions removed.</param>
/// <param name="Truncated">
/// Whether more addresses answered than <c>maxResponders</c> allowed to be reported.
/// </param>
/// <param name="Responders">The addresses that answered, in address order.</param>
internal sealed record RangeSweepResult(
    [property: JsonPropertyName("walk")] string? Walk,
    [property: JsonPropertyName("firstAddress")] string? FirstAddress,
    [property: JsonPropertyName("lastAddress")] string? LastAddress,
    [property: JsonPropertyName("scanned")] int Scanned,
    [property: JsonPropertyName("excluded")] int Excluded,
    [property: JsonPropertyName("truncated")] bool Truncated,
    [property: JsonPropertyName("responders")] IReadOnlyList<RangeSweepResponder>? Responders);

/// <summary>One address that answered.</summary>
/// <param name="Address">The address.</param>
/// <param name="RttMilliseconds">
/// The round trip of the fastest reply, or nothing if the reply arrived without one being timed.
/// </param>
internal sealed record RangeSweepResponder(
    [property: JsonPropertyName("address")] string? Address,
    [property: JsonPropertyName("rttMilliseconds")] double? RttMilliseconds);
