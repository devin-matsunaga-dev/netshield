using System.Globalization;
using System.Text.Json;

using NetShield.IntegrationTests.Identity;

namespace NetShield.IntegrationTests.Inventory;

/// <summary>
/// Drives one whole reachability round trip: lease the queued probe, report a result as the
/// collector would, and deliver the event that lands it on the device.
/// </summary>
/// <remarks>
/// The result payloads here are written as JSON by hand rather than through the API's own type,
/// deliberately. What the API has to cope with is what a separate process in another language
/// actually sends, so the test writes that — a shape the C# side never constructs is a shape the
/// C# side cannot accidentally make agree with itself.
/// </remarks>
internal static class ReachabilityFixtures
{
    private const string Jobs = "/internal/collector/jobs?collector=collector-test";
    private const string Results = "/internal/collector/results";

    /// <summary>
    /// Leases the outstanding probe, reports a probe that ran, and delivers the outbox.
    /// </summary>
    /// <param name="host">The host under test.</param>
    /// <param name="sent">How many echo requests the probe says it sent.</param>
    /// <param name="received">How many replies it says came back.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public static Task CompleteOneAsync(
        InventoryHost host,
        int sent,
        int received,
        CancellationToken cancellationToken) =>
        CompleteAsync(host, ProbeResult(sent, received), outcome: "Succeeded", cancellationToken);

    /// <summary>
    /// Leases the outstanding probe and reports that the collector could not perform it.
    /// </summary>
    /// <remarks>
    /// The distinction the package rests on: this must leave the device's state alone, however
    /// many times it happens.
    /// </remarks>
    public static Task FailOneAsync(
        InventoryHost host,
        string detail,
        CancellationToken cancellationToken) =>
        CompleteAsync(host, data: null, outcome: "Failed", cancellationToken, detail);

    /// <summary>Reports a result whose payload is not a probe result at all.</summary>
    public static Task CompleteWithAsync(
        InventoryHost host,
        string dataJson,
        CancellationToken cancellationToken) =>
        CompleteAsync(host, dataJson, outcome: "Succeeded", cancellationToken);

    /// <summary>
    /// The payload <c>collector/icmp/executor.py</c> produces, written as that process writes it.
    /// </summary>
    public static string ProbeResult(int sent, int received)
    {
        IEnumerable<string> replies = Enumerable.Range(0, sent).Select(sequence =>
            sequence < received
                ? $$"""{ "sequence": {{sequence}}, "rttMilliseconds": {{(sequence + 1) * 2}}.0 }"""
                : $$"""{ "sequence": {{sequence}}, "rttMilliseconds": null }""");

        double loss = sent == 0 ? 100 : Math.Round(100.0 * (sent - received) / sent, 2);

        string summary = received == 0
            ? """
              "rttMillisecondsMin": null, "rttMillisecondsMax": null, "rttMillisecondsAvg": null
              """
            : $$"""
                "rttMillisecondsMin": 2.0,
                "rttMillisecondsMax": {{received * 2}}.0,
                "rttMillisecondsAvg": {{(received + 1).ToString(CultureInfo.InvariantCulture)}}.0
                """;

        return $$"""
                 {
                   "probe": "icmp",
                   "address": "10.10.0.1",
                   "sent": {{sent}},
                   "received": {{received}},
                   "lossPercent": {{loss.ToString(CultureInfo.InvariantCulture)}},
                   {{summary}},
                   "replies": [{{string.Join(", ", replies)}}]
                 }
                 """;
    }

    private static async Task CompleteAsync(
        InventoryHost host,
        string? data,
        string outcome,
        CancellationToken cancellationToken,
        string? detail = null)
    {
        ApiResponse leased = await host.Collector.GetAsync(Jobs, cancellationToken);

        JsonElement jobs = leased.Json.GetProperty("jobs");

        if (jobs.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("There was no queued probe to lease.");
        }

        JsonElement job = jobs[0];

        string body = $$"""
                        {
                          "collector": "collector-test",
                          "results": [
                            {
                              "jobId": "{{job.GetProperty("jobId").GetGuid()}}",
                              "leaseToken": "{{job.GetProperty("leaseToken").GetString()}}",
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
