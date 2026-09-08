using System.Text.Json;

using FluentAssertions;

using NetShield.Inventory.Topology;

namespace NetShield.UnitTests.Inventory;

/// <summary>
/// The topology walk payloads, as they are written into and read out of a job row.
/// </summary>
/// <remarks>
/// The API's half of a contract whose other half is in <c>netshield-collector</c>, with no
/// generator between them. The collector suite pins the same names from its side; this pins them
/// from here, so a member renamed on either side fails a test rather than quietly stopping being
/// read.
/// </remarks>
public sealed class TopologyPayloadTests
{
    [Fact]
    public void NeighborWalkParameters_AreWrittenWithTheNamesTheCollectorReads()
    {
        string json = JsonSerializer.Serialize(
            NeighborWalkParameters.From(new TopologyOptions()),
            TopologySerializerContext.Default.NeighborWalkParameters);

        using JsonDocument document = JsonDocument.Parse(json);

        Names(document).Should().BeEquivalentTo(
            "walk",
            "timeoutSeconds",
            "retries",
            "maxRepetitions",
            "maxRows",
            "maxNeighbors");

        document.RootElement.GetProperty("walk").GetString().Should().Be("neighbors");
    }

    [Fact]
    public void RouteWalkParameters_AreWrittenWithTheNamesTheCollectorReads()
    {
        string json = JsonSerializer.Serialize(
            RouteWalkParameters.From(new TopologyOptions()),
            TopologySerializerContext.Default.RouteWalkParameters);

        using JsonDocument document = JsonDocument.Parse(json);

        Names(document).Should().BeEquivalentTo(
            "walk",
            "timeoutSeconds",
            "retries",
            "maxRepetitions",
            "maxRows",
            "maxNextHops");

        document.RootElement.GetProperty("walk").GetString().Should().Be("routes");
    }

    [Fact]
    public void TheRouteWalk_ReadsFarMoreThanItReports()
    {
        // The shape of the reduction: read a great many routes, report the handful of distinct
        // gateways behind them. If these ever crossed, the payload would be the routing table.
        TopologyOptions options = new();

        options.MaxRouteRows.Should().BeGreaterThan(options.MaxNextHops * 10);
    }

    [Fact]
    public void TheRouteWalk_WaitsLongerPerRequestThanTheNeighbourWalk()
    {
        // A device assembling a GETBULK response over a large routing table takes measurably
        // longer than one answering a handful of LLDP rows.
        TopologyOptions options = new();

        options.RouteRequestTimeoutSeconds.Should().BeGreaterThan(options.RequestTimeoutSeconds);
    }

    [Fact]
    public void TheRouteWalk_RunsLessOftenThanTheNeighbourWalk()
    {
        // What a device is cabled to changes when somebody re-cables it; what it routes through
        // changes when somebody re-designs the network.
        TopologyOptions options = new();

        options.RouteWalkIntervalSeconds.Should().BeGreaterThan(options.NeighborWalkIntervalSeconds);
    }

    [Fact]
    public void ANeighborWalkResult_ReadsTheDocumentTheCollectorWrites()
    {
        // The exact shape collector/snmp/neighbors.py:payload emits, written out by hand so that
        // this test fails if either side moves a name.
        const string Payload = """
            {
              "walk": "neighbors",
              "localChassisId": "00:1C:73:00:00:01",
              "localChassisIdKind": "MacAddress",
              "localSystemName": "core-sw-1",
              "lldpSupported": true,
              "lldpCount": 1,
              "lldpTruncated": false,
              "lldp": [
                {
                  "localIfIndex": 1,
                  "localPortName": "Et1",
                  "chassisId": "00:1C:73:00:00:02",
                  "chassisIdKind": "MacAddress",
                  "portId": "Gi0/1",
                  "portIdKind": "InterfaceName",
                  "portDescription": "GigabitEthernet0/1",
                  "systemName": "acc-sw-1",
                  "systemDescription": "Cisco IOS",
                  "managementAddress": "192.0.2.12",
                  "capabilities": 28
                }
              ],
              "cdpSupported": false,
              "cdpCount": 0,
              "cdpTruncated": false,
              "cdp": []
            }
            """;

        NeighborWalkResult? result = JsonSerializer.Deserialize(
            Payload,
            TopologySerializerContext.Default.NeighborWalkResult);

        result.Should().NotBeNull();
        result!.Walk.Should().Be("neighbors");
        result.LocalChassisId.Should().Be("00:1C:73:00:00:01");
        result.LldpSupported.Should().BeTrue();
        result.CdpSupported.Should().BeFalse();
        result.Lldp.Should().ContainSingle();
        result.Lldp![0].ChassisId.Should().Be("00:1C:73:00:00:02");
        result.Lldp[0].ManagementAddress.Should().Be("192.0.2.12");
        result.Lldp[0].LocalIfIndex.Should().Be(1);
    }

    [Fact]
    public void ARouteWalkResult_ReadsTheDocumentTheCollectorWrites()
    {
        const string Payload = """
            {
              "walk": "routes",
              "routesSupported": true,
              "routeTable": "inetCidrRoute",
              "routeCount": 5,
              "nextHopCount": 1,
              "nextHopsTruncated": false,
              "nextHops": [
                {
                  "address": "192.0.2.11",
                  "ifIndex": 1,
                  "localPortName": "Et1",
                  "routeCount": 2
                }
              ]
            }
            """;

        RouteWalkResult? result = JsonSerializer.Deserialize(
            Payload,
            TopologySerializerContext.Default.RouteWalkResult);

        result.Should().NotBeNull();
        result!.RoutesSupported.Should().BeTrue();
        result.RouteTable.Should().Be("inetCidrRoute");
        result.RouteCount.Should().Be(5);
        result.NextHops.Should().ContainSingle();
        result.NextHops![0].Address.Should().Be("192.0.2.11");
        result.NextHops[0].RouteCount.Should().Be(2);
    }

    [Fact]
    public void AnUnsupportedProtocol_IsReadAsUnsupportedRatherThanEmpty()
    {
        // The distinction the whole aging rule rests on.
        const string Payload = """
            {
              "walk": "neighbors",
              "lldpSupported": false,
              "lldpCount": 0,
              "lldpTruncated": false,
              "lldp": [],
              "cdpSupported": false,
              "cdpCount": 0,
              "cdpTruncated": false,
              "cdp": []
            }
            """;

        NeighborWalkResult? result = JsonSerializer.Deserialize(
            Payload,
            TopologySerializerContext.Default.NeighborWalkResult);

        result!.LldpSupported.Should().BeFalse();
        result.CdpSupported.Should().BeFalse();
    }

    private static IEnumerable<string> Names(JsonDocument document) =>
        document.RootElement.EnumerateObject().Select(property => property.Name);
}
