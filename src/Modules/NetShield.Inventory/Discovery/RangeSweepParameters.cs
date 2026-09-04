using System.Text.Json.Serialization;

namespace NetShield.Inventory.Discovery;

/// <summary>
/// What a range sweep tells the collector to do, as it is written into
/// <c>collector_jobs.parameters</c>.
/// </summary>
/// <remarks>
/// <para>
/// The second shape a <c>Discover</c> job can carry, and the reason WP-1.5 put a discriminator
/// in the first one. <see cref="Walk"/> is that discriminator: a fingerprint walk says
/// <c>snmp</c> and a range sweep says <c>sweep</c>, the two sit in the same table looking
/// identical from the outside, and each side runs the one it recognises and refuses the other.
/// The collector's wire contract is untouched — no <c>CollectorJobKind</c> member, no field on
/// the lease, the result or the heartbeat.
/// </para>
/// <para>
/// The job carries a span rather than a CIDR block because a job's slice of a seed is not
/// generally a prefix, and it carries the exclusions rather than a pre-filtered address list
/// because a list of 256 addresses is 4 kB of the 16 kB a parameters document may hold.
/// </para>
/// <para>
/// A sweep names no credential and no device. An echo request authenticates to nothing, and the
/// whole point of the job is that nothing here is a device yet.
/// </para>
/// </remarks>
/// <param name="Walk">Always <see cref="WalkName"/> on a row this package wrote.</param>
/// <param name="FirstAddress">The first address to probe.</param>
/// <param name="LastAddress">The last address to probe, inclusive.</param>
/// <param name="Exclusions">
/// Blocks inside that span that must never be probed, in CIDR notation. Applied by the collector.
/// </param>
/// <param name="Count">How many echo requests one address gets before it is called silent.</param>
/// <param name="TimeoutSeconds">How long one address's replies are waited for.</param>
/// <param name="IntervalSeconds">How long to wait between one address's requests.</param>
/// <param name="Concurrency">How many addresses are probed at once.</param>
/// <param name="MaxResponders">
/// The most responders one result will carry, so that a span where everything answers cannot
/// produce a payload larger than <c>CollectorLimits.ResultLength</c>.
/// </param>
internal sealed record RangeSweepParameters(
    [property: JsonPropertyName("walk")] string Walk,
    [property: JsonPropertyName("firstAddress")] string FirstAddress,
    [property: JsonPropertyName("lastAddress")] string LastAddress,
    [property: JsonPropertyName("exclusions")] IReadOnlyList<string> Exclusions,
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("timeoutSeconds")] double TimeoutSeconds,
    [property: JsonPropertyName("intervalSeconds")] double IntervalSeconds,
    [property: JsonPropertyName("concurrency")] int Concurrency,
    [property: JsonPropertyName("maxResponders")] int MaxResponders)
{
    /// <summary>
    /// The discriminator identifying a range sweep, on the parameters going out and on the
    /// result coming back.
    /// </summary>
    internal const string WalkName = "sweep";

    /// <summary>The parameters for one span, swept at the configured settings.</summary>
    internal static RangeSweepParameters From(
        DiscoveryOptions options,
        AddressSpan span,
        IReadOnlyList<string> exclusions)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(exclusions);

        return new RangeSweepParameters(
            WalkName,
            span.FirstAddress.ToString(),
            span.LastAddress.ToString(),
            exclusions,
            options.SweepProbeCount,
            options.SweepTimeoutSeconds,
            options.SweepIntervalSeconds,
            options.SweepConcurrency,
            options.MaxRespondersPerJob);
    }
}
