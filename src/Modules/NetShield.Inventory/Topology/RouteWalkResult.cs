using System.Text.Json.Serialization;

namespace NetShield.Inventory.Topology;

/// <summary>
/// What a route walk found, as it is written into <c>collector_jobs.result</c>.
/// </summary>
/// <remarks>
/// Already reduced. The collector reads the whole table and reports one record per distinct
/// gateway with the number of routes that named it — the same choice WP-1.8 made for
/// <c>macCountOnPort</c>, and for the same reason: the count is a property of the whole table,
/// and the API only ever sees what survived the reduction, so counting here would say a default
/// route and a full transit table were the same weight of evidence.
/// </remarks>
/// <param name="Walk">The discriminator, matching <see cref="RouteWalkParameters.WalkName"/>.</param>
/// <param name="RoutesSupported">Whether the device answered a routing table at all.</param>
/// <param name="RouteTable">Which of the three tables answered.</param>
/// <param name="RouteCount">How many routes it held, before the reduction.</param>
/// <param name="NextHopCount">How many distinct gateways they reduced to.</param>
/// <param name="NextHopsTruncated">Whether more gateways existed than the result carries.</param>
/// <param name="NextHops">The gateways themselves.</param>
internal sealed record RouteWalkResult(
    [property: JsonPropertyName("walk")] string? Walk,
    [property: JsonPropertyName("routesSupported")] bool RoutesSupported,
    [property: JsonPropertyName("routeTable")] string? RouteTable,
    [property: JsonPropertyName("routeCount")] int RouteCount,
    [property: JsonPropertyName("nextHopCount")] int NextHopCount,
    [property: JsonPropertyName("nextHopsTruncated")] bool NextHopsTruncated,
    [property: JsonPropertyName("nextHops")] IReadOnlyList<RouteNextHopEntry>? NextHops);

/// <summary>One gateway a device forwards through.</summary>
/// <param name="Address">The gateway's address.</param>
/// <param name="IfIndex">The interface the route leaves by, where the table said.</param>
/// <param name="LocalPortName">What the device calls that interface.</param>
/// <param name="RouteCount">How many routes named this gateway — the weight of the evidence.</param>
internal sealed record RouteNextHopEntry(
    [property: JsonPropertyName("address")] string? Address,
    [property: JsonPropertyName("ifIndex")] int? IfIndex,
    [property: JsonPropertyName("localPortName")] string? LocalPortName,
    [property: JsonPropertyName("routeCount")] int? RouteCount);
