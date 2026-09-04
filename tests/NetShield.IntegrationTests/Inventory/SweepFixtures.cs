using System.Globalization;
using System.Net;
using System.Text.Json;

using NetShield.Contracts.Inventory;

using NetShield.IntegrationTests.Identity;

namespace NetShield.IntegrationTests.Inventory;

/// <summary>
/// Drives one whole discovery round trip: seed a range, start a run, lease every sweep job it
/// queued, report what answered as the collector would, and deliver the events that turn the
/// answers into candidates.
/// </summary>
/// <remarks>
/// The result payloads are written as JSON by hand rather than through the API's own type, for
/// the reason <see cref="DiscoveryFixtures"/> and <c>ReachabilityFixtures</c> do the same: what
/// the API has to cope with is what a separate process in another language actually sends, and a
/// shape the C# side constructs is a shape the C# side cannot fail to agree with.
/// </remarks>
internal static class SweepFixtures
{
    private const string Jobs = "/internal/collector/jobs?collector=collector-test";
    private const string Results = "/internal/collector/results";

    /// <summary>Creates a discovery seed.</summary>
    public static async Task<Guid> CreateSeedAsync(
        InventoryHost host,
        CancellationToken cancellationToken,
        string name = "Lab",
        IReadOnlyList<string>? ranges = null,
        IReadOnlyList<string>? exclusions = null,
        bool enabled = true,
        int intervalMinutes = 60)
    {
        ApiResponse created = await host.Client.PostAsync(
            "/api/v1/discovery/seeds",
            new CreateDiscoverySeedRequest(
                name,
                Description: null,
                enabled,
                ranges ?? ["192.0.2.0/29"],
                exclusions,
                intervalMinutes),
            cancellationToken);

        if (created.Status != 201)
        {
            throw new InvalidOperationException($"The seed was not created: {created.Status} {created.Body}");
        }

        return created.Json.GetProperty("id").GetGuid();
    }

    /// <summary>Asks for a run of a seed.</summary>
    public static Task<ApiResponse> RequestRunAsync(
        InventoryHost host,
        Guid seedId,
        CancellationToken cancellationToken) =>
        host.Client.PostAsync($"/api/v1/discovery/runs?seedId={seedId}", new { }, cancellationToken);

    /// <summary>Starts a run and returns its id.</summary>
    public static async Task<Guid> StartRunAsync(
        InventoryHost host,
        Guid seedId,
        CancellationToken cancellationToken)
    {
        ApiResponse queued = await RequestRunAsync(host, seedId, cancellationToken);

        if (queued.Status != 202)
        {
            throw new InvalidOperationException($"The run was not queued: {queued.Status} {queued.Body}");
        }

        return queued.Json.GetProperty("runId").GetGuid();
    }

    /// <summary>
    /// Leases every queued sweep job and reports that the addresses in
    /// <paramref name="responders"/> answered.
    /// </summary>
    /// <remarks>
    /// Each job is answered for its own span only, which is what a collector would do: a run over
    /// several spans is several jobs, and a job that claimed a responder outside its own span
    /// would be reporting on work it was not given.
    /// </remarks>
    /// <returns>How many sweep jobs were answered.</returns>
    public static Task<int> CompleteSweepsAsync(
        InventoryHost host,
        IReadOnlyList<string> responders,
        CancellationToken cancellationToken) =>
        CompleteAsync(host, responders, succeed: true, cancellationToken);

    /// <summary>Leases every queued sweep job and reports that the collector could not run it.</summary>
    public static Task<int> FailSweepsAsync(
        InventoryHost host,
        CancellationToken cancellationToken) =>
        CompleteAsync(host, [], succeed: false, cancellationToken);

    /// <summary>Seeds a range, runs it, and reports what answered — the whole round trip.</summary>
    public static async Task<Guid> SweepAsync(
        InventoryHost host,
        Guid seedId,
        IReadOnlyList<string> responders,
        CancellationToken cancellationToken)
    {
        Guid runId = await StartRunAsync(host, seedId, cancellationToken);

        await CompleteSweepsAsync(host, responders, cancellationToken);

        return runId;
    }

    /// <summary>
    /// Leases every queued sweep job and returns the token each was claimed under.
    /// </summary>
    /// <remarks>
    /// A lease is a claim on every job it returns, so a test that wants to answer a run's jobs
    /// one at a time has to lease once and keep the tokens — asking again would find the rest
    /// already claimed by the first call, which is exactly what the lease is for.
    /// </remarks>
    public static async Task<IReadOnlyDictionary<Guid, string>> LeaseSweepsAsync(
        InventoryHost host,
        CancellationToken cancellationToken)
    {
        ApiResponse leased = await host.Collector.GetAsync(Jobs, cancellationToken);

        return leased.Json.GetProperty("jobs").EnumerateArray()
            .Where(IsSweep)
            .ToDictionary(
                job => job.GetProperty("jobId").GetGuid(),
                job => job.GetProperty("leaseToken").GetString()!);
    }

    /// <summary>Reports one leased sweep job, and delivers what the result set off.</summary>
    public static async Task SubmitAsync(
        InventoryHost host,
        Guid jobId,
        string leaseToken,
        string? data,
        CancellationToken cancellationToken)
    {
        string body = $$"""
                        {
                          "collector": "collector-test",
                          "results": [
                            {
                              "jobId": "{{jobId}}",
                              "leaseToken": "{{leaseToken}}",
                              "outcome": "{{(data is null ? "Failed" : "Succeeded")}}",
                              "detail": {{(data is null ? "\"No ICMP socket could be opened.\"" : "null")}},
                              "data": {{data ?? "null"}}
                            }
                          ]
                        }
                        """;

        ApiResponse acknowledged = await host.Collector.PostRawAsync(Results, body, cancellationToken);

        if (acknowledged.Status != 200
            || acknowledged.Json.GetProperty("accepted").GetArrayLength() != 1)
        {
            throw new InvalidOperationException(
                $"The result was not accepted: {acknowledged.Status} {acknowledged.Body}");
        }

        await host.DispatchOutboxAsync(cancellationToken);
    }

    private static bool IsSweep(JsonElement job) =>
        job.GetProperty("kind").GetString() == "Discover"
        && job.TryGetProperty("parameters", out JsonElement parameters)
        && parameters.ValueKind == JsonValueKind.Object
        && parameters.TryGetProperty("walk", out JsonElement walk)
        && walk.GetString() == "sweep";

    private static async Task<int> CompleteAsync(
        InventoryHost host,
        IReadOnlyList<string> responders,
        bool succeed,
        CancellationToken cancellationToken)
    {
        ApiResponse leased = await host.Collector.GetAsync(Jobs, cancellationToken);

        List<JsonElement> sweeps = [.. leased.Json.GetProperty("jobs").EnumerateArray().Where(IsSweep)];

        if (sweeps.Count == 0)
        {
            throw new InvalidOperationException("There was no queued sweep to lease.");
        }

        IEnumerable<string> reports = sweeps.Select(job => Report(job, responders, succeed));

        string body = $$"""
                        {
                          "collector": "collector-test",
                          "results": [{{string.Join(",", reports)}}]
                        }
                        """;

        ApiResponse acknowledged = await host.Collector.PostRawAsync(Results, body, cancellationToken);

        if (acknowledged.Status != 200
            || acknowledged.Json.GetProperty("accepted").GetArrayLength() != sweeps.Count)
        {
            throw new InvalidOperationException(
                $"The results were not accepted: {acknowledged.Status} {acknowledged.Body}");
        }

        await host.DispatchOutboxAsync(cancellationToken);

        return sweeps.Count;
    }

    private static string Report(JsonElement job, IReadOnlyList<string> responders, bool succeed)
    {
        JsonElement parameters = job.GetProperty("parameters");

        string first = parameters.GetProperty("firstAddress").GetString()!;
        string last = parameters.GetProperty("lastAddress").GetString()!;

        string data = succeed
            ? SweepResult(first, last, [.. responders.Where(address => Within(address, first, last))])
            : "null";

        return $$"""
                 {
                   "jobId": "{{job.GetProperty("jobId").GetGuid()}}",
                   "leaseToken": "{{job.GetProperty("leaseToken").GetString()}}",
                   "outcome": "{{(succeed ? "Succeeded" : "Failed")}}",
                   "detail": {{(succeed ? "null" : "\"No ICMP socket could be opened.\"")}},
                   "data": {{data}}
                 }
                 """;
    }

    /// <summary>
    /// The payload <c>collector/discovery/executor.py</c> produces, written as that process
    /// writes it.
    /// </summary>
    public static string SweepResult(
        string firstAddress,
        string lastAddress,
        IReadOnlyList<string> responders,
        int? scanned = null,
        int excluded = 0,
        bool truncated = false,
        string walk = "sweep")
    {
        IEnumerable<string> rows = responders.Select((address, index) => $$"""
            {
              "address": {{JsonSerializer.Serialize(address)}},
              "rttMilliseconds": {{(1.5 + index).ToString("0.0", CultureInfo.InvariantCulture)}}
            }
            """);

        int span = (int)(Number(lastAddress) - Number(firstAddress) + 1);

        return $$"""
                 {
                   "walk": {{JsonSerializer.Serialize(walk)}},
                   "firstAddress": {{JsonSerializer.Serialize(firstAddress)}},
                   "lastAddress": {{JsonSerializer.Serialize(lastAddress)}},
                   "scanned": {{(scanned ?? (span - excluded)).ToString(CultureInfo.InvariantCulture)}},
                   "excluded": {{excluded.ToString(CultureInfo.InvariantCulture)}},
                   "truncated": {{(truncated ? "true" : "false")}},
                   "responders": [{{string.Join(",", rows)}}]
                 }
                 """;
    }

    private static bool Within(string address, string first, string last) =>
        Number(address) >= Number(first) && Number(address) <= Number(last);

    /// <summary>An IPv4 address as a number, which is all these fixtures sweep.</summary>
    private static long Number(string address)
    {
        byte[] bytes = IPAddress.Parse(address).GetAddressBytes();
        long number = 0;

        foreach (byte value in bytes)
        {
            number = (number << 8) | value;
        }

        return number;
    }
}
