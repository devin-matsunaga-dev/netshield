using System.Globalization;
using System.Text.Json;

using NetShield.IntegrationTests.Identity;

namespace NetShield.IntegrationTests.Inventory;

/// <summary>
/// Drives one whole client-walk round trip: ask for a walk, lease it, report a result as the
/// collector would, and deliver the event that folds it into the interval tables.
/// </summary>
/// <remarks>
/// The result payloads here are written as JSON by hand rather than through the API's own type,
/// deliberately, for the reason <see cref="DiscoveryFixtures"/> and
/// <see cref="ReachabilityFixtures"/> do the same: what the API has to cope with is what a
/// separate process in another language actually sends, and a shape the C# side constructs is a
/// shape the C# side cannot fail to agree with.
/// </remarks>
internal static class ClientFixtures
{
    private const string Jobs = "/internal/collector/jobs?collector=collector-test";
    private const string Results = "/internal/collector/results";

    /// <summary>Asks for a read of a device's client tables.</summary>
    public static Task<ApiResponse> RequestWalkAsync(
        InventoryHost host,
        Guid deviceId,
        CancellationToken cancellationToken) =>
        host.Client.PostAsync($"/api/v1/devices/{deviceId}/client-walk", new { }, cancellationToken);

    /// <summary>Asks for a walk, reports a result, and delivers the outbox.</summary>
    public static async Task WalkAsync(
        InventoryHost host,
        Guid deviceId,
        string data,
        CancellationToken cancellationToken)
    {
        ApiResponse queued = await RequestWalkAsync(host, deviceId, cancellationToken);

        if (queued.Status != 202)
        {
            throw new InvalidOperationException($"The walk was not queued: {queued.Status} {queued.Body}");
        }

        await CompleteAsync(host, data, outcome: "Succeeded", cancellationToken);
    }

    /// <summary>Asks for a walk and reports that the collector could not perform it.</summary>
    public static async Task FailWalkAsync(
        InventoryHost host,
        Guid deviceId,
        string detail,
        CancellationToken cancellationToken)
    {
        ApiResponse queued = await RequestWalkAsync(host, deviceId, cancellationToken);

        if (queued.Status != 202)
        {
            throw new InvalidOperationException($"The walk was not queued: {queued.Status} {queued.Body}");
        }

        await CompleteAsync(host, data: null, outcome: "Failed", cancellationToken, detail);
    }

    /// <summary>One entry of a neighbour cache, as the collector reports it.</summary>
    public static string Neighbor(string ipAddress, string macAddress, int ifIndex = 10) =>
        $$"""
          {
            "ipAddress": {{JsonSerializer.Serialize(ipAddress)}},
            "macAddress": {{JsonSerializer.Serialize(macAddress)}},
            "ifIndex": {{ifIndex.ToString(CultureInfo.InvariantCulture)}}
          }
          """;

    /// <summary>One entry of a forwarding database, as the collector reports it.</summary>
    public static string Forwarding(
        string macAddress,
        int ifIndex,
        int? vlanId = 10,
        int? macCountOnPort = 1) =>
        $$"""
          {
            "macAddress": {{JsonSerializer.Serialize(macAddress)}},
            "ifIndex": {{ifIndex.ToString(CultureInfo.InvariantCulture)}},
            "vlanId": {{Number(vlanId)}},
            "macCountOnPort": {{Number(macCountOnPort)}}
          }
          """;

    /// <summary>
    /// The payload <c>collector/snmp/clients.py</c> produces, written as that process writes it.
    /// </summary>
    /// <remarks>
    /// The two <c>supported</c> flags default to whether anything was passed, which is what the
    /// collector actually does — but each can be set independently, because "this device
    /// implements no forwarding database" and "its forwarding database is empty" are the
    /// distinction the whole handler turns on.
    /// </remarks>
    public static string WalkResult(
        IReadOnlyList<string>? neighbors = null,
        IReadOnlyList<string>? forwarding = null,
        bool? neighborsSupported = null,
        bool? forwardingSupported = null,
        bool neighborsTruncated = false,
        bool forwardingTruncated = false,
        string walk = "clients")
    {
        IReadOnlyList<string> arp = neighbors ?? [];
        IReadOnlyList<string> fdb = forwarding ?? [];

        return $$"""
                 {
                   "walk": {{JsonSerializer.Serialize(walk)}},
                   "neighborsSupported": {{Bool(neighborsSupported ?? arp.Count > 0)}},
                   "neighborCount": {{arp.Count.ToString(CultureInfo.InvariantCulture)}},
                   "neighborsTruncated": {{Bool(neighborsTruncated)}},
                   "neighbors": [{{string.Join(",", arp)}}],
                   "forwardingSupported": {{Bool(forwardingSupported ?? fdb.Count > 0)}},
                   "forwardingCount": {{fdb.Count.ToString(CultureInfo.InvariantCulture)}},
                   "forwardingTruncated": {{Bool(forwardingTruncated)}},
                   "forwarding": [{{string.Join(",", fdb)}}]
                 }
                 """;
    }

    private static string Bool(bool value) => value ? "true" : "false";

    private static string Number(int? value) =>
        value is null ? "null" : value.Value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Leases the queued walk, without reporting anything about it yet.
    /// </summary>
    /// <remarks>
    /// The two halves are separable so a test can do something between them — the window in
    /// which a device is removed while its walk is in flight is a real one, and it is the only
    /// way the handler's own liveness check is reachable: the lease itself already fails a job
    /// naming a device that has gone (WP-1.3), so the device has to go *after* the lease.
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
            throw new InvalidOperationException("There was no queued client walk to lease.");
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
                              "detail": {{(detail is null ? "null" : JsonSerializer.Serialize(detail))}},
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

}
