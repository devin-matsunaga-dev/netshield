using System.Globalization;
using System.Text.Json;

using NetShield.Contracts.Inventory;

using NetShield.IntegrationTests.Collector;
using NetShield.IntegrationTests.Identity;

namespace NetShield.IntegrationTests.Inventory;

/// <summary>
/// Drives one whole fingerprint round trip: ask for a walk, lease it, report a result as the
/// collector would, and deliver the event that lands it on the device.
/// </summary>
/// <remarks>
/// The result payloads here are written as JSON by hand rather than through the API's own type,
/// deliberately, for the reason <see cref="ReachabilityFixtures"/> does the same: what the API
/// has to cope with is what a separate process in another language actually sends, and a shape
/// the C# side constructs is a shape the C# side cannot fail to agree with.
/// </remarks>
internal static class DiscoveryFixtures
{
    private const string Jobs = "/internal/collector/jobs?collector=collector-test";
    private const string Results = "/internal/collector/results";

    /// <summary>Creates a device with an SNMPv2c profile assigned, ready to be walked.</summary>
    public static async Task<Guid> CreateWalkableDeviceAsync(
        InventoryHost host,
        CancellationToken cancellationToken,
        string hostname = "switch-01",
        string address = "10.10.0.1")
    {
        Guid deviceId = await CollectorFixtures.CreateDeviceAsync(host, hostname, address, cancellationToken);

        Guid profileId = await CollectorFixtures.CreateCredentialProfileAsync(
            host,
            $"{hostname} SNMP",
            "fixture-community",
            cancellationToken);

        await AssignAsync(host, deviceId, [profileId], cancellationToken);

        return deviceId;
    }

    /// <summary>Replaces a device's credential profile assignments.</summary>
    public static async Task AssignAsync(
        InventoryHost host,
        Guid deviceId,
        IReadOnlyList<Guid> profileIds,
        CancellationToken cancellationToken)
    {
        ApiResponse set = await host.Client.PutAsync(
            $"/api/v1/devices/{deviceId}/credential-profiles",
            new SetDeviceCredentialProfilesRequest(profileIds),
            cancellationToken);

        if (set.Status != 200)
        {
            throw new InvalidOperationException($"Could not assign profiles: {set.Status} {set.Body}");
        }
    }

    /// <summary>Asks for a walk of a device.</summary>
    public static Task<ApiResponse> RequestWalkAsync(
        InventoryHost host,
        Guid deviceId,
        CancellationToken cancellationToken) =>
        host.Client.PostAsync($"/api/v1/devices/{deviceId}/walk", new { }, cancellationToken);

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

    /// <summary>
    /// The payload <c>collector/snmp/executor.py</c> produces, written as that process writes it.
    /// </summary>
    public static string WalkResult(
        string vendor = "CiscoIos",
        bool reducedCapability = false,
        string? sysObjectId = "1.3.6.1.4.1.9.1.2494",
        string? sysDescr = "Cisco IOS Software, Version 15.2(7)E3, RELEASE SOFTWARE (fc2)",
        string? sysName = "lab-sw-ios-01",
        string? model = "WS-C2960X-48FPD-L",
        string? osVersion = "15.2(7)E3",
        string? serialNumber = "FOC1234X5YZ",
        IReadOnlyList<int>? interfaces = null,
        bool truncated = false,
        int? interfaceCount = null,
        int operStatus = 1)
    {
        IReadOnlyList<int> indexes = interfaces ?? [1, 2];

        IEnumerable<string> rows = indexes.Select(index => $$"""
            {
              "index": {{index.ToString(CultureInfo.InvariantCulture)}},
              "name": "Gi0/{{index}}",
              "description": "GigabitEthernet0/{{index}}",
              "alias": "port {{index}}",
              "interfaceType": 6,
              "mtu": 1500,
              "speedBitsPerSecond": 1000000000,
              "physicalAddress": "00:1A:2B:3C:4D:0{{index}}",
              "adminStatus": 1,
              "operStatus": {{operStatus.ToString(CultureInfo.InvariantCulture)}}
            }
            """);

        return $$"""
                 {
                   "walk": "snmp",
                   "vendor": {{Text(vendor)}},
                   "reducedCapability": {{(reducedCapability ? "true" : "false")}},
                   "sysObjectId": {{Text(sysObjectId)}},
                   "sysDescr": {{Text(sysDescr)}},
                   "sysName": {{Text(sysName)}},
                   "sysContact": "netops@example.invalid",
                   "sysLocation": "Lab rack 3",
                   "uptimeSeconds": 1234567.89,
                   "model": {{Text(model)}},
                   "osVersion": {{Text(osVersion)}},
                   "serialNumber": {{Text(serialNumber)}},
                   "interfaceCount": {{(interfaceCount ?? indexes.Count).ToString(CultureInfo.InvariantCulture)}},
                   "interfacesTruncated": {{(truncated ? "true" : "false")}},
                   "interfaces": [{{string.Join(",", rows)}}]
                 }
                 """;
    }

    private static string Text(string? value) => value is null ? "null" : JsonSerializer.Serialize(value);

    private static async Task CompleteAsync(
        InventoryHost host,
        string? data,
        string outcome,
        CancellationToken cancellationToken,
        string? detail = null)
    {
        ApiResponse leased = await host.Collector.GetAsync(Jobs, cancellationToken);

        JsonElement jobs = leased.Json.GetProperty("jobs");

        JsonElement? walk = jobs.EnumerateArray()
            .Cast<JsonElement?>()
            .FirstOrDefault(job => job!.Value.GetProperty("kind").GetString() == "Discover");

        if (walk is null)
        {
            throw new InvalidOperationException("There was no queued walk to lease.");
        }

        string body = $$"""
                        {
                          "collector": "collector-test",
                          "results": [
                            {
                              "jobId": "{{walk.Value.GetProperty("jobId").GetGuid()}}",
                              "leaseToken": "{{walk.Value.GetProperty("leaseToken").GetString()}}",
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
}
