using System.Globalization;
using System.Text.Json;

using NetShield.IntegrationTests.Identity;

namespace NetShield.IntegrationTests.Inventory;

/// <summary>
/// Drives a whole topology round trip: ask for a walk, lease it, report a result as the collector
/// would, and deliver the event that folds it into observations and edges.
/// </summary>
/// <remarks>
/// The result payloads here are written as JSON by hand rather than through the API's own type,
/// deliberately, for the reason <see cref="ClientFixtures"/> and <see cref="DiscoveryFixtures"/>
/// do the same: what the API has to cope with is what a separate process in another language
/// actually sends, and a shape the C# side constructs is a shape the C# side cannot fail to agree
/// with.
/// </remarks>
internal static class TopologyFixtures
{
    private const string Jobs = "/internal/collector/jobs?collector=collector-test";
    private const string Results = "/internal/collector/results";

    /// <summary>Asks for a read of a device's neighbour protocols.</summary>
    public static Task<ApiResponse> RequestNeighborWalkAsync(
        InventoryHost host,
        Guid deviceId,
        CancellationToken cancellationToken) =>
        host.Client.PostAsync($"/api/v1/devices/{deviceId}/neighbor-walk", new { }, cancellationToken);

    /// <summary>Asks for a read of a device's routing table.</summary>
    public static Task<ApiResponse> RequestRouteWalkAsync(
        InventoryHost host,
        Guid deviceId,
        CancellationToken cancellationToken) =>
        host.Client.PostAsync($"/api/v1/devices/{deviceId}/route-walk", new { }, cancellationToken);

    /// <summary>Asks for a neighbour walk, reports a result, and delivers the outbox.</summary>
    public static async Task NeighborWalkAsync(
        InventoryHost host,
        Guid deviceId,
        string data,
        CancellationToken cancellationToken)
    {
        await QueueAsync(await RequestNeighborWalkAsync(host, deviceId, cancellationToken));
        await CompleteAsync(host, data, outcome: "Succeeded", cancellationToken);
    }

    /// <summary>Asks for a route walk, reports a result, and delivers the outbox.</summary>
    public static async Task RouteWalkAsync(
        InventoryHost host,
        Guid deviceId,
        string data,
        CancellationToken cancellationToken)
    {
        await QueueAsync(await RequestRouteWalkAsync(host, deviceId, cancellationToken));
        await CompleteAsync(host, data, outcome: "Succeeded", cancellationToken);
    }

    /// <summary>Asks for a neighbour walk and reports that the collector could not perform it.</summary>
    public static async Task FailNeighborWalkAsync(
        InventoryHost host,
        Guid deviceId,
        string detail,
        CancellationToken cancellationToken)
    {
        await QueueAsync(await RequestNeighborWalkAsync(host, deviceId, cancellationToken));
        await CompleteAsync(host, data: null, outcome: "Failed", cancellationToken, detail);
    }

    /// <summary>One entry of an LLDP remote table, as the collector reports it.</summary>
    public static string Lldp(
        int localIfIndex,
        string chassisId,
        string? portId = null,
        string chassisIdKind = "MacAddress",
        string portIdKind = "InterfaceName",
        string? systemName = null,
        string? managementAddress = null,
        string? localPortName = null) =>
        $$"""
          {
            "localIfIndex": {{Number(localIfIndex)}},
            "localPortName": {{Text(localPortName)}},
            "chassisId": {{Text(chassisId)}},
            "chassisIdKind": {{Text(chassisIdKind)}},
            "portId": {{Text(portId)}},
            "portIdKind": {{Text(portIdKind)}},
            "portDescription": null,
            "systemName": {{Text(systemName)}},
            "systemDescription": null,
            "managementAddress": {{Text(managementAddress)}},
            "capabilities": 28
          }
          """;

    /// <summary>One entry of a CDP cache, as the collector reports it.</summary>
    public static string Cdp(
        int localIfIndex,
        string deviceId,
        string? devicePort = null,
        string? address = null,
        string? localPortName = null) =>
        $$"""
          {
            "localIfIndex": {{Number(localIfIndex)}},
            "localPortName": {{Text(localPortName)}},
            "deviceId": {{Text(deviceId)}},
            "devicePort": {{Text(devicePort)}},
            "platform": "Fixture Platform",
            "version": null,
            "address": {{Text(address)}},
            "capabilities": 40
          }
          """;

    /// <summary>One reduced gateway, as the collector reports it.</summary>
    public static string NextHop(
        string address,
        int ifIndex,
        int routeCount = 1,
        string? localPortName = null) =>
        $$"""
          {
            "address": {{Text(address)}},
            "ifIndex": {{Number(ifIndex)}},
            "localPortName": {{Text(localPortName)}},
            "routeCount": {{Number(routeCount)}}
          }
          """;

    /// <summary>
    /// The payload <c>collector/snmp/neighbors.py</c> produces, written as that process writes it.
    /// </summary>
    /// <remarks>
    /// Each <c>supported</c> flag defaults to whether anything was passed, which is what the
    /// collector actually does — and each can be set independently, because "this device
    /// implements no CDP cache" and "its CDP cache is empty" are the distinction the whole aging
    /// rule turns on.
    /// </remarks>
    public static string NeighborResult(
        IReadOnlyList<string>? lldp = null,
        IReadOnlyList<string>? cdp = null,
        bool? lldpSupported = null,
        bool? cdpSupported = null,
        bool lldpTruncated = false,
        bool cdpTruncated = false,
        string? localChassisId = null,
        string? localSystemName = null,
        string walk = "neighbors")
    {
        IReadOnlyList<string> neighbors = lldp ?? [];
        IReadOnlyList<string> cisco = cdp ?? [];

        return $$"""
                 {
                   "walk": {{Text(walk)}},
                   "localChassisId": {{Text(localChassisId)}},
                   "localChassisIdKind": "MacAddress",
                   "localSystemName": {{Text(localSystemName)}},
                   "lldpSupported": {{Bool(lldpSupported ?? neighbors.Count > 0)}},
                   "lldpCount": {{Number(neighbors.Count)}},
                   "lldpTruncated": {{Bool(lldpTruncated)}},
                   "lldp": [{{string.Join(",", neighbors)}}],
                   "cdpSupported": {{Bool(cdpSupported ?? cisco.Count > 0)}},
                   "cdpCount": {{Number(cisco.Count)}},
                   "cdpTruncated": {{Bool(cdpTruncated)}},
                   "cdp": [{{string.Join(",", cisco)}}]
                 }
                 """;
    }

    /// <summary>The payload <c>collector/snmp/routes.py</c> produces.</summary>
    public static string RouteResult(
        IReadOnlyList<string>? nextHops = null,
        bool? routesSupported = null,
        bool truncated = false,
        int routeCount = 0,
        string? routeTable = "inetCidrRoute",
        string walk = "routes")
    {
        IReadOnlyList<string> hops = nextHops ?? [];

        return $$"""
                 {
                   "walk": {{Text(walk)}},
                   "routesSupported": {{Bool(routesSupported ?? hops.Count > 0)}},
                   "routeTable": {{Text(routeTable)}},
                   "routeCount": {{Number(routeCount == 0 ? hops.Count : routeCount)}},
                   "nextHopCount": {{Number(hops.Count)}},
                   "nextHopsTruncated": {{Bool(truncated)}},
                   "nextHops": [{{string.Join(",", hops)}}]
                 }
                 """;
    }

    /// <summary>
    /// Leases the queued walk, without reporting anything about it yet.
    /// </summary>
    /// <remarks>
    /// The two halves are separable so a test can do something between them — the window in which
    /// a device is removed while its walk is in flight is a real one, and it is the only way the
    /// handlers' own liveness check is reachable: the lease itself already fails a job naming a
    /// device that has gone (WP-1.3), so the device has to go <em>after</em> the lease.
    /// </remarks>
    public static async Task<LeasedWalk> LeaseAsync(
        InventoryHost host,
        CancellationToken cancellationToken)
    {
        ApiResponse leased = await host.Collector.GetAsync(Jobs, cancellationToken);

        JsonElement? walk = leased.Json.GetProperty("jobs").EnumerateArray()
            .Cast<JsonElement?>()
            .FirstOrDefault(job => job!.Value.GetProperty("kind").GetString() == "Discover");

        if (walk is null)
        {
            throw new InvalidOperationException("There was no queued topology walk to lease.");
        }

        return new LeasedWalk(
            walk.Value.GetProperty("jobId").GetGuid(),
            walk.Value.GetProperty("leaseToken").GetString()!);
    }

    /// <summary>Reports a result for a walk already leased, and delivers the outbox.</summary>
    public static async Task ReportAsync(
        InventoryHost host,
        LeasedWalk walk,
        string? data,
        CancellationToken cancellationToken,
        string outcome = "Succeeded",
        string? detail = null)
    {
        string body = $$"""
                        {
                          "collector": "collector-test",
                          "results": [
                            {
                              "jobId": "{{walk.JobId}}",
                              "leaseToken": "{{walk.LeaseToken}}",
                              "outcome": "{{outcome}}",
                              "detail": {{Text(detail)}},
                              "data": {{data ?? "null"}}
                            }
                          ]
                        }
                        """;

        ApiResponse acknowledged = await host.Collector.PostRawAsync(Results, body, cancellationToken);

        if (acknowledged.Status != 200 || acknowledged.Json.GetProperty("accepted").GetArrayLength() != 1)
        {
            throw new InvalidOperationException(
                $"The result was not accepted: {acknowledged.Status} {acknowledged.Body}");
        }

        await host.DispatchOutboxAsync(cancellationToken);
    }

    /// <summary>One leased walk, as the collector holds it.</summary>
    internal sealed record LeasedWalk(Guid JobId, string LeaseToken);

    private static Task QueueAsync(ApiResponse queued) =>
        queued.Status == 202
            ? Task.CompletedTask
            : throw new InvalidOperationException(
                $"The walk was not queued: {queued.Status} {queued.Body}");

    private static async Task CompleteAsync(
        InventoryHost host,
        string? data,
        string outcome,
        CancellationToken cancellationToken,
        string? detail = null)
    {
        LeasedWalk walk = await LeaseAsync(host, cancellationToken);

        await ReportAsync(host, walk, data, cancellationToken, outcome, detail);
    }

    private static string Bool(bool value) => value ? "true" : "false";

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Text(string? value) =>
        value is null ? "null" : JsonSerializer.Serialize(value);
}
