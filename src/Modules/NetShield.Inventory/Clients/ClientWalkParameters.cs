using System.Text.Json.Serialization;

namespace NetShield.Inventory.Clients;

/// <summary>
/// What a client walk tells the collector to do, as it is written into
/// <c>collector_jobs.parameters</c>.
/// </summary>
/// <remarks>
/// <para>
/// The third walk a <c>Discover</c> job can be, beside WP-1.5's <c>snmp</c> fingerprint and
/// WP-1.6's <c>sweep</c>, and the collector's wire contract is untouched by it for the reason
/// theirs were: WP-1.3 defined the envelope and left <c>parameters</c> and <c>data</c> opaque so
/// that the package owning a walk could fill them in. No <c>CollectorJobKind</c> member is added
/// and no field of the lease, result or heartbeat moves.
/// </para>
/// <para>
/// It reads two tables and neither of them is the other's: a router answers the neighbour cache
/// and forwards nothing, an access switch answers the forwarding database and has no ARP table
/// worth reading, and a layer-3 switch answers both. So a walk asks for both and reports which
/// it got, rather than the API deciding in advance what a device ought to have — a decision it
/// would have to make from <c>DeviceRole</c>, which is free text an operator typed.
/// </para>
/// </remarks>
/// <param name="Walk">Always <see cref="WalkName"/> on a row this package wrote.</param>
/// <param name="TimeoutSeconds">How long one request waits for an answer.</param>
/// <param name="Retries">How many times a request is repeated before it is given up on.</param>
/// <param name="MaxRepetitions">How many rows one GETBULK asks for.</param>
/// <param name="MaxRows">The most objects one subtree walk will read, whatever the device offers.</param>
/// <param name="MaxNeighbors">The most ARP entries one result will carry.</param>
/// <param name="MaxForwardingEntries">The most forwarding-database entries one result will carry.</param>
internal sealed record ClientWalkParameters(
    [property: JsonPropertyName("walk")] string Walk,
    [property: JsonPropertyName("timeoutSeconds")] double TimeoutSeconds,
    [property: JsonPropertyName("retries")] int Retries,
    [property: JsonPropertyName("maxRepetitions")] int MaxRepetitions,
    [property: JsonPropertyName("maxRows")] int MaxRows,
    [property: JsonPropertyName("maxNeighbors")] int MaxNeighbors,
    [property: JsonPropertyName("maxForwardingEntries")] int MaxForwardingEntries)
{
    /// <summary>
    /// The discriminator identifying a client-table walk, on the parameters going out and on the
    /// result coming back. Pinned to the collector's own constant by
    /// <c>JobDiscriminatorParityTests</c>.
    /// </summary>
    internal const string WalkName = "clients";

    /// <summary>The parameters for a walk run at the configured settings.</summary>
    internal static ClientWalkParameters From(ClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new ClientWalkParameters(
            WalkName,
            options.RequestTimeoutSeconds,
            options.Retries,
            options.MaxRepetitions,
            options.MaxRowsPerSubtree,
            options.MaxNeighbors,
            options.MaxForwardingEntries);
    }
}
