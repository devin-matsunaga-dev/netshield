using System.Text.Json.Serialization;

namespace NetShield.Inventory.Topology;

/// <summary>
/// What a neighbour walk tells the collector to do, as it is written into
/// <c>collector_jobs.parameters</c>.
/// </summary>
/// <remarks>
/// The fourth walk a <c>Discover</c> job can be, beside WP-1.5's <c>snmp</c>, WP-1.6's
/// <c>sweep</c> and WP-1.8's <c>clients</c>, and the collector's wire contract is untouched by it
/// for the reason theirs were: WP-1.3 defined the envelope and left <c>parameters</c> and
/// <c>data</c> opaque so that the package owning a walk could fill them in. No
/// <c>CollectorJobKind</c> member is added and no field of the lease, result or heartbeat moves.
///
/// It says nothing about <em>which</em> protocols to read. That is the vendor's, declared by the
/// adapter in <c>collector/vendors/</c> — the API deciding it here would be a vendor
/// <c>if</c> chain with a network hop in the middle of it.
/// </remarks>
/// <param name="Walk">Always <see cref="WalkName"/> on a row this package wrote.</param>
/// <param name="TimeoutSeconds">How long one request waits for an answer.</param>
/// <param name="Retries">How many times a request is repeated before it is given up on.</param>
/// <param name="MaxRepetitions">How many rows one GETBULK asks for.</param>
/// <param name="MaxRows">The most objects one subtree walk will read, whatever the device offers.</param>
/// <param name="MaxNeighbors">The most entries one result will carry, per protocol.</param>
internal sealed record NeighborWalkParameters(
    [property: JsonPropertyName("walk")] string Walk,
    [property: JsonPropertyName("timeoutSeconds")] double TimeoutSeconds,
    [property: JsonPropertyName("retries")] int Retries,
    [property: JsonPropertyName("maxRepetitions")] int MaxRepetitions,
    [property: JsonPropertyName("maxRows")] int MaxRows,
    [property: JsonPropertyName("maxNeighbors")] int MaxNeighbors)
{
    /// <summary>
    /// The discriminator identifying a neighbour walk, on the parameters going out and on the
    /// result coming back. Pinned to the collector's own constant by
    /// <c>JobDiscriminatorParityTests</c>.
    /// </summary>
    internal const string WalkName = "neighbors";

    /// <summary>The parameters for a walk run at the configured settings.</summary>
    internal static NeighborWalkParameters From(TopologyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new NeighborWalkParameters(
            WalkName,
            options.RequestTimeoutSeconds,
            options.Retries,
            options.MaxRepetitions,
            options.MaxRowsPerSubtree,
            options.MaxNeighbors);
    }
}
