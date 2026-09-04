using System.Net;
using System.Net.Sockets;
using System.Text.Json;

using FluentAssertions;

using NetShield.Inventory.Discovery;

namespace NetShield.UnitTests.Inventory;

/// <summary>
/// The two range-sweep payloads, as JSON. They are the contract between this repository and
/// <c>netshield-collector</c>, with no generator between them, so the member names are asserted
/// literally: a rename here has to break a test rather than quietly stop being read there.
/// </summary>
public sealed class RangeSweepPayloadTests
{
    [Fact]
    public void Parameters_AreWrittenWithTheNamesTheCollectorReads()
    {
        RangeSweepParameters parameters = RangeSweepParameters.From(
            new DiscoveryOptions(),
            Span("10.0.0.1", "10.0.0.8"),
            ["10.0.0.4/31"]);

        using JsonDocument document = JsonSerializer.SerializeToDocument(
            parameters,
            DiscoverySerializerContext.Default.RangeSweepParameters);

        JsonElement root = document.RootElement;

        root.GetProperty("walk").GetString().Should().Be("sweep");
        root.GetProperty("firstAddress").GetString().Should().Be("10.0.0.1");
        root.GetProperty("lastAddress").GetString().Should().Be("10.0.0.8");
        root.GetProperty("exclusions").EnumerateArray().Single().GetString().Should().Be("10.0.0.4/31");
        root.GetProperty("count").GetInt32().Should().Be(1);
        root.GetProperty("timeoutSeconds").GetDouble().Should().Be(1);
        root.GetProperty("intervalSeconds").GetDouble().Should().Be(0);
        root.GetProperty("concurrency").GetInt32().Should().Be(64);
        root.GetProperty("maxResponders").GetInt32().Should().Be(1024);
    }

    [Fact]
    public void Parameters_NameTheSweepWalkAndNotTheFingerprintOne()
    {
        // The discriminator is the whole reason two kinds of Discover can share a table.
        RangeSweepParameters.WalkName.Should().NotBe(SnmpWalkParameters.WalkName);

        RangeSweepParameters.From(new DiscoveryOptions(), Span("10.0.0.1", "10.0.0.1"), [])
            .Walk.Should().Be(RangeSweepParameters.WalkName);
    }

    [Fact]
    public void Result_ReadsThePayloadTheCollectorWrites()
    {
        // Written the way collector/discovery/executor.py writes it, by hand.
        const string Payload = """
                               {
                                 "walk": "sweep",
                                 "firstAddress": "10.0.0.1",
                                 "lastAddress": "10.0.0.4",
                                 "scanned": 3,
                                 "excluded": 1,
                                 "truncated": false,
                                 "responders": [
                                   { "address": "10.0.0.2", "rttMilliseconds": 1.5 },
                                   { "address": "10.0.0.3", "rttMilliseconds": null }
                                 ]
                               }
                               """;

        RangeSweepResult? result = JsonSerializer.Deserialize(
            Payload,
            DiscoverySerializerContext.Default.RangeSweepResult);

        result.Should().NotBeNull();
        result!.Walk.Should().Be("sweep");
        result.FirstAddress.Should().Be("10.0.0.1");
        result.LastAddress.Should().Be("10.0.0.4");
        result.Scanned.Should().Be(3);
        result.Excluded.Should().Be(1);
        result.Truncated.Should().BeFalse();
        result.Responders.Should().HaveCount(2);
        result.Responders![0].Address.Should().Be("10.0.0.2");
        result.Responders[0].RttMilliseconds.Should().Be(1.5);
        result.Responders[1].RttMilliseconds.Should().BeNull();
    }

    [Fact]
    public void Result_WithNoResponders_ReadsAsAnEmptyList()
    {
        // A span where nothing answered is a successful job with nothing in it, which is a real
        // observation about the range rather than a failure.
        const string Payload = """
                               {
                                 "walk": "sweep",
                                 "firstAddress": "10.0.0.1",
                                 "lastAddress": "10.0.0.4",
                                 "scanned": 4,
                                 "excluded": 0,
                                 "truncated": false,
                                 "responders": []
                               }
                               """;

        JsonSerializer.Deserialize(Payload, DiscoverySerializerContext.Default.RangeSweepResult)!
            .Responders.Should().BeEmpty();
    }

    [Fact]
    public void Result_OfAFingerprintWalk_DoesNotNameTheSweep()
    {
        // What the result handler filters on: a Discover carrying the other walk's payload is
        // not this package's row to read.
        RangeSweepResult? result = JsonSerializer.Deserialize(
            """{ "walk": "snmp", "vendor": "CiscoIos" }""",
            DiscoverySerializerContext.Default.RangeSweepResult);

        result!.Walk.Should().NotBe(RangeSweepParameters.WalkName);
    }

    private static AddressSpan Span(string first, string last) =>
        new(
            AddressFamily.InterNetwork,
            AddressRange.ToNumber(IPAddress.Parse(first)),
            AddressRange.ToNumber(IPAddress.Parse(last)));
}
