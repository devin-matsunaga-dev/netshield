using System.Text.Json;

using FluentAssertions;

using NetShield.Inventory.Reachability;

namespace NetShield.UnitTests.Inventory;

/// <summary>
/// The two payloads that cross between the API and <c>netshield-collector</c>.
/// </summary>
/// <remarks>
/// There is no generator between the C# shapes and the Python ones — the collector contract is
/// deliberately absent from the OpenAPI document, because a lease carries an opened credential
/// and that must never enter a contract a browser is built from (WP-1.3). So the two sides are
/// two hand-written copies, and the property names are the whole of the agreement between them.
/// These tests pin those names on this side; <c>tests/test_icmp_executor.py</c> pins the same
/// names on the other. A rename that breaks the agreement fails one gate or the other.
/// </remarks>
public sealed class ReachabilityPayloadTests
{
    private static readonly ReachabilityOptions Options = new()
    {
        ProbeCount = 4,
        ProbeTimeoutSeconds = 2,
        ProbeIntervalSeconds = 0.25
    };

    [Fact]
    public void Parameters_FromOptions_CarryTheProbeNameAndTheThreeConfiguredValues()
    {
        IcmpProbeParameters parameters = IcmpProbeParameters.From(Options);

        parameters.Probe.Should().Be("icmp");
        parameters.Count.Should().Be(4);
        parameters.TimeoutSeconds.Should().Be(2);
        parameters.IntervalSeconds.Should().Be(0.25);
    }

    [Fact]
    public void Parameters_Serialized_UseTheNamesTheCollectorReads()
    {
        string json = JsonSerializer.Serialize(
            IcmpProbeParameters.From(Options),
            ReachabilitySerializerContext.Default.IcmpProbeParameters);

        using JsonDocument document = JsonDocument.Parse(json);

        document.RootElement.EnumerateObject().Select(member => member.Name)
            .Should().BeEquivalentTo("probe", "count", "timeoutSeconds", "intervalSeconds");
    }

    [Fact]
    public void Result_AsTheCollectorWritesIt_RoundTripsIntoTheShapeTheHandlerReads()
    {
        // Byte-for-byte what collector/icmp/executor.py produces for a four-request probe that
        // lost one reply. If this stops deserialising, the two hand-written shapes have drifted.
        const string FromTheCollector = """
            {
              "probe": "icmp",
              "address": "10.0.0.1",
              "sent": 4,
              "received": 3,
              "lossPercent": 25.0,
              "rttMillisecondsMin": 1.5,
              "rttMillisecondsMax": 3.5,
              "rttMillisecondsAvg": 2.5,
              "replies": [
                { "sequence": 0, "rttMilliseconds": 1.5 },
                { "sequence": 1, "rttMilliseconds": null },
                { "sequence": 2, "rttMilliseconds": 2.5 },
                { "sequence": 3, "rttMilliseconds": 3.5 }
              ]
            }
            """;

        IcmpProbeResult? result = JsonSerializer.Deserialize(
            FromTheCollector,
            ReachabilitySerializerContext.Default.IcmpProbeResult);

        result.Should().NotBeNull();
        result!.Probe.Should().Be("icmp");
        result.Address.Should().Be("10.0.0.1");
        result.Sent.Should().Be(4);
        result.Received.Should().Be(3);
        result.LossPercent.Should().Be(25.0);
        result.RttMillisecondsAvg.Should().Be(2.5);

        // "RTT recorded per probe": one entry per request, with the unanswered one present and
        // null rather than missing from the list.
        result.Replies.Should().HaveCount(4);
        result.Replies![1].RttMilliseconds.Should().BeNull();
        result.Replies.Select(reply => reply.Sequence).Should().Equal(0, 1, 2, 3);
    }

    [Fact]
    public void Result_OfATotallyUnansweredProbe_CarriesNoRoundTripsAndFullLoss()
    {
        const string Silent = """
            {
              "probe": "icmp",
              "address": "10.0.0.1",
              "sent": 4,
              "received": 0,
              "lossPercent": 100.0,
              "rttMillisecondsMin": null,
              "rttMillisecondsMax": null,
              "rttMillisecondsAvg": null,
              "replies": [
                { "sequence": 0, "rttMilliseconds": null },
                { "sequence": 1, "rttMilliseconds": null },
                { "sequence": 2, "rttMilliseconds": null },
                { "sequence": 3, "rttMilliseconds": null }
              ]
            }
            """;

        IcmpProbeResult? result = JsonSerializer.Deserialize(
            Silent,
            ReachabilitySerializerContext.Default.IcmpProbeResult);

        result.Should().NotBeNull();
        result!.Received.Should().Be(0);
        result.RttMillisecondsAvg.Should().BeNull();
    }
}
