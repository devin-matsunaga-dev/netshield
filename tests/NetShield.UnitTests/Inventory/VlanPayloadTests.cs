using System.Text.Json;

using FluentAssertions;

using NetShield.Inventory.Topology;

namespace NetShield.UnitTests.Inventory;

/// <summary>
/// The VLAN walk payload, as it is written into and read out of a job row, and the settings that
/// shape the walk.
/// </summary>
/// <remarks>
/// The API's half of a contract whose other half is in <c>netshield-collector</c>, with no
/// generator between them. <c>tests/test_snmp_vlan_executor.py</c> pins the same names from its
/// side; this pins them from here, so a member renamed on either side fails a test rather than
/// quietly stopping being read.
/// </remarks>
public sealed class VlanPayloadTests
{
    [Fact]
    public void VlanWalkParameters_AreWrittenWithTheNamesTheCollectorReads()
    {
        string json = JsonSerializer.Serialize(
            VlanWalkParameters.From(new TopologyOptions()),
            TopologySerializerContext.Default.VlanWalkParameters);

        using JsonDocument document = JsonDocument.Parse(json);

        Names(document).Should().BeEquivalentTo(
            "walk",
            "timeoutSeconds",
            "retries",
            "maxRepetitions",
            "maxRows",
            "maxVlans");

        document.RootElement.GetProperty("walk").GetString().Should().Be("vlans");
    }

    [Fact]
    public void AVlanWalkResult_ReadsTheDocumentTheCollectorWrites()
    {
        // The exact shape collector/snmp/vlans.py:payload emits, written out by hand so that this
        // test fails if either side moves a name.
        const string Payload = """
            {
              "walk": "vlans",
              "vlansSupported": true,
              "vlanTable": "dot1qVlanStatic",
              "vlanCount": 2,
              "vlansTruncated": false,
              "vlans": [
                {
                  "vlanId": 10,
                  "name": "Server VLAN",
                  "ifIndexes": [1, 2],
                  "untaggedIfIndexes": [1],
                  "portCount": 2,
                  "unresolvedPortCount": 0
                },
                {
                  "vlanId": 20,
                  "name": "Users VLAN",
                  "ifIndexes": [1, 2, 3],
                  "untaggedIfIndexes": [3],
                  "portCount": 4,
                  "unresolvedPortCount": 1
                }
              ]
            }
            """;

        VlanWalkResult? result = JsonSerializer.Deserialize(
            Payload,
            TopologySerializerContext.Default.VlanWalkResult);

        result.Should().NotBeNull();
        result!.Walk.Should().Be("vlans");
        result.VlansSupported.Should().BeTrue();
        result.VlanTable.Should().Be("dot1qVlanStatic");
        result.VlanCount.Should().Be(2);
        result.Vlans.Should().HaveCount(2);
        result.Vlans![0].VlanId.Should().Be(10);
        result.Vlans[0].Name.Should().Be("Server VLAN");
        result.Vlans[0].IfIndexes.Should().Equal(1, 2);
        result.Vlans[0].UntaggedIfIndexes.Should().Equal(1);
        result.Vlans[1].UnresolvedPortCount.Should().Be(1);
    }

    [Fact]
    public void ADeviceWithNoVlanTable_IsReadAsUnsupportedRatherThanEmpty()
    {
        // The distinction the whole withdrawal rule rests on: a router implements neither
        // Q-BRIDGE table, and reading that as "this device has no VLANs" would empty the
        // inventory of every device that is not a bridge.
        const string Payload = """
            {
              "walk": "vlans",
              "vlansSupported": false,
              "vlanTable": null,
              "vlanCount": 0,
              "vlansTruncated": false,
              "vlans": []
            }
            """;

        VlanWalkResult? result = JsonSerializer.Deserialize(
            Payload,
            TopologySerializerContext.Default.VlanWalkResult);

        result!.VlansSupported.Should().BeFalse();
        result.VlanTable.Should().BeNull();
        result.Vlans.Should().BeEmpty();
    }

    [Fact]
    public void AVlanFromTheFallbackTable_HasNoName()
    {
        const string Payload = """
            {
              "walk": "vlans",
              "vlansSupported": true,
              "vlanTable": "dot1qVlanCurrent",
              "vlanCount": 1,
              "vlansTruncated": false,
              "vlans": [
                {
                  "vlanId": 20,
                  "name": null,
                  "ifIndexes": [1],
                  "untaggedIfIndexes": [1],
                  "portCount": 1,
                  "unresolvedPortCount": 0
                }
              ]
            }
            """;

        VlanWalkResult? result = JsonSerializer.Deserialize(
            Payload,
            TopologySerializerContext.Default.VlanWalkResult);

        result!.VlanTable.Should().Be("dot1qVlanCurrent");
        result.Vlans![0].Name.Should().BeNull();
    }

    // --- the settings ------------------------------------------------------------------------

    [Fact]
    public void TheVlanWalk_RunsNoMoreOftenThanTheRouteWalk()
    {
        // A VLAN is a design decision. Reading one every fifteen minutes would be five hundred
        // devices' worth of SNMP to confirm something that changes a few times a year.
        TopologyOptions options = new();

        options.VlanWalkIntervalSeconds.Should().BeGreaterThanOrEqualTo(
            options.RouteWalkIntervalSeconds);

        options.VlanWalkIntervalSeconds.Should().BeGreaterThan(options.NeighborWalkIntervalSeconds);
    }

    [Fact]
    public void TheVlanWalk_WaitsLongerPerRequestThanTheNeighbourWalk()
    {
        // Q-BRIDGE is the table set an old or half-implemented agent is likeliest to be slow on,
        // and the neighbour walk's threshold would fail those devices on every pass.
        TopologyOptions options = new();

        options.VlanRequestTimeoutSeconds.Should().BeGreaterThan(options.RequestTimeoutSeconds);
    }

    [Fact]
    public void TheVlanCeiling_IsNoHigherThanTheStandardsOwn()
    {
        // 802.1Q admits 4,094 VLANs, so a device cannot be truncated by a limit lower than the
        // standard's unless an operator sets one deliberately.
        new TopologyOptions().MaxVlans.Should().BeLessThanOrEqualTo(4_094);
    }

    private static IEnumerable<string> Names(JsonDocument document) =>
        document.RootElement.EnumerateObject().Select(property => property.Name);
}
