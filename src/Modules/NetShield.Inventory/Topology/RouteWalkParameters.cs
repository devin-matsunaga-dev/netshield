using System.Text.Json.Serialization;

namespace NetShield.Inventory.Topology;

/// <summary>
/// What a route walk tells the collector to do, as it is written into
/// <c>collector_jobs.parameters</c>.
/// </summary>
/// <remarks>
/// The fifth walk a <c>Discover</c> job can be, and the second this package owns. It is separate
/// from the neighbour walk on purpose: LLDP and CDP are small, bounded and predictable, and a
/// routing table is none of the three. One combined walk would mean a device that answered its
/// neighbour protocols and then timed out reading forty thousand routes failed the whole job and
/// discarded topology it had already established — WP-1.8's known trade, at a far worse ratio.
/// Two walks cost a second schedule and buy failure isolation.
///
/// Its row ceiling is much higher than the neighbour walk's and its report ceiling much lower,
/// which is the shape of the reduction: read a great many routes, report the handful of distinct
/// gateways behind them.
/// </remarks>
/// <param name="Walk">Always <see cref="WalkName"/> on a row this package wrote.</param>
/// <param name="TimeoutSeconds">How long one request waits for an answer.</param>
/// <param name="Retries">How many times a request is repeated before it is given up on.</param>
/// <param name="MaxRepetitions">How many rows one GETBULK asks for.</param>
/// <param name="MaxRows">The most objects one subtree walk will read, whatever the device offers.</param>
/// <param name="MaxNextHops">The most distinct gateways one result will carry.</param>
internal sealed record RouteWalkParameters(
    [property: JsonPropertyName("walk")] string Walk,
    [property: JsonPropertyName("timeoutSeconds")] double TimeoutSeconds,
    [property: JsonPropertyName("retries")] int Retries,
    [property: JsonPropertyName("maxRepetitions")] int MaxRepetitions,
    [property: JsonPropertyName("maxRows")] int MaxRows,
    [property: JsonPropertyName("maxNextHops")] int MaxNextHops)
{
    /// <summary>
    /// The discriminator identifying a route walk. Pinned to the collector's own constant by
    /// <c>JobDiscriminatorParityTests</c>.
    /// </summary>
    internal const string WalkName = "routes";

    /// <summary>The parameters for a walk run at the configured settings.</summary>
    internal static RouteWalkParameters From(TopologyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new RouteWalkParameters(
            WalkName,
            options.RouteRequestTimeoutSeconds,
            options.Retries,
            options.MaxRepetitions,
            options.MaxRouteRows,
            options.MaxNextHops);
    }
}
