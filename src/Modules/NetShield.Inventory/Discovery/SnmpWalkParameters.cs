using System.Text.Json.Serialization;

namespace NetShield.Inventory.Discovery;

/// <summary>
/// What a fingerprint job tells the collector to do, as it is written into
/// <c>collector_jobs.parameters</c>.
/// </summary>
/// <remarks>
/// <para>
/// The second job kind to be given a shape, and the collector's wire contract is untouched by it
/// for the reason WP-1.4's was: WP-1.3 defined the envelope and left <c>parameters</c> and
/// <c>data</c> opaque so that the package owning a kind could fill them in. A fingerprint walk is
/// a <see cref="NetShield.Contracts.Collector.CollectorJobKind.Discover"/>, which is what that
/// member has named since WP-1.3, and no member of the enum and no field of the lease, result or
/// heartbeat has moved.
/// </para>
/// <para>
/// <see cref="Walk"/> is the discriminator, and it is the reason this is not simply five numbers.
/// WP-1.6's range sweep will be a <c>Discover</c> too and will sit in the same table looking
/// identical from the outside; the collector runs the walk it recognises and refuses the rest,
/// and the result handler here reads only rows whose parameters say <c>snmp</c>.
/// </para>
/// </remarks>
/// <param name="Walk">Always <see cref="WalkName"/> on a row this package wrote.</param>
/// <param name="TimeoutSeconds">How long one request waits for an answer.</param>
/// <param name="Retries">How many times a request is repeated before it is given up on.</param>
/// <param name="MaxRepetitions">How many rows one GETBULK asks for.</param>
/// <param name="MaxRows">The most objects one subtree walk will read, whatever the device offers.</param>
/// <param name="MaxInterfaces">The most interfaces one result will carry.</param>
internal sealed record SnmpWalkParameters(
    [property: JsonPropertyName("walk")] string Walk,
    [property: JsonPropertyName("timeoutSeconds")] double TimeoutSeconds,
    [property: JsonPropertyName("retries")] int Retries,
    [property: JsonPropertyName("maxRepetitions")] int MaxRepetitions,
    [property: JsonPropertyName("maxRows")] int MaxRows,
    [property: JsonPropertyName("maxInterfaces")] int MaxInterfaces)
{
    /// <summary>
    /// The discriminator identifying an SNMP fingerprint walk, on the parameters going out and on
    /// the result coming back.
    /// </summary>
    internal const string WalkName = "snmp";

    /// <summary>The parameters for a walk run at the configured settings.</summary>
    internal static SnmpWalkParameters From(DiscoveryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new SnmpWalkParameters(
            WalkName,
            options.RequestTimeoutSeconds,
            options.Retries,
            options.MaxRepetitions,
            options.MaxRowsPerSubtree,
            options.MaxInterfaces);
    }
}
