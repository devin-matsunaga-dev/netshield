using System.Text.Json.Serialization;

namespace NetShield.Inventory.Reachability;

/// <summary>
/// What a reachability job tells the collector to do, as it is written into
/// <c>collector_jobs.parameters</c>.
/// </summary>
/// <remarks>
/// <para>
/// WP-1.3 left the parameter document of every job kind opaque — "a shape invented before the
/// first kind exists would be a shape the first kind has to work around". This is the first kind,
/// and this is its shape. The collector's job contract is otherwise untouched: a reachability job
/// is a <c>Poll</c>, which is what <c>CollectorJobKind.Poll</c> has said it covers since WP-1.3,
/// and no member of that enum and no field of the lease, result or heartbeat has moved.
/// </para>
/// <para>
/// <see cref="Probe"/> is the discriminator, and it is the reason this shape is not simply three
/// numbers. A <c>Poll</c> row queued by this package and a <c>Poll</c> row queued by the SNMP
/// metric polling in Phase 3 will sit in one table looking identical, and each has to be able to
/// recognise its own: the result handler here reads rows whose parameters say <c>icmp</c> and
/// leaves every other row to whoever queued it.
/// </para>
/// </remarks>
/// <param name="Probe">Always <see cref="ProbeName"/> on a row this package wrote.</param>
/// <param name="Count">How many echo requests to send.</param>
/// <param name="TimeoutSeconds">How long to wait for the last reply.</param>
/// <param name="IntervalSeconds">How long to wait between requests.</param>
internal sealed record IcmpProbeParameters(
    [property: JsonPropertyName("probe")] string Probe,
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("timeoutSeconds")] double TimeoutSeconds,
    [property: JsonPropertyName("intervalSeconds")] double IntervalSeconds)
{
    /// <summary>
    /// The discriminator identifying an ICMP reachability probe, on the parameters going out and
    /// on the result coming back.
    /// </summary>
    internal const string ProbeName = "icmp";

    /// <summary>The parameters for a probe run at the configured settings.</summary>
    internal static IcmpProbeParameters From(ReachabilityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new IcmpProbeParameters(
            ProbeName,
            options.ProbeCount,
            options.ProbeTimeoutSeconds,
            options.ProbeIntervalSeconds);
    }
}
