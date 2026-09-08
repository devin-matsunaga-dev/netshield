using System.Text.Json;

using FluentAssertions;

using NetShield.Inventory.Clients;

namespace NetShield.UnitTests.Inventory;

/// <summary>
/// The two client-walk payloads that cross between the API and <c>netshield-collector</c>.
/// </summary>
/// <remarks>
/// There is no generator between the C# shapes and the Python ones — the collector contract is
/// deliberately absent from the OpenAPI document (WP-1.3) — so the two sides are two hand-written
/// copies and the property names are the whole of the agreement between them. These tests pin
/// those names on this side; <c>tests/test_snmp_client_executor.py</c> pins the same names on the
/// other. A rename that breaks the agreement fails one gate or the other.
/// </remarks>
public sealed class ClientPayloadTests
{
    private static readonly ClientOptions Options = new()
    {
        RequestTimeoutSeconds = 5,
        Retries = 2,
        MaxRepetitions = 25,
        MaxRowsPerSubtree = 20_000,
        MaxNeighbors = 4_096,
        MaxForwardingEntries = 8_192
    };

    [Fact]
    public void Parameters_FromOptions_CarryTheWalkNameAndTheConfiguredValues()
    {
        ClientWalkParameters parameters = ClientWalkParameters.From(Options);

        parameters.Walk.Should().Be("clients");
        parameters.TimeoutSeconds.Should().Be(5);
        parameters.Retries.Should().Be(2);
        parameters.MaxRepetitions.Should().Be(25);
        parameters.MaxRows.Should().Be(20_000);
        parameters.MaxNeighbors.Should().Be(4_096);
        parameters.MaxForwardingEntries.Should().Be(8_192);
    }

    [Fact]
    public void Parameters_Serialise_WithTheNamesTheCollectorReads()
    {
        string json = JsonSerializer.Serialize(
            ClientWalkParameters.From(Options),
            ClientSerializerContext.Default.ClientWalkParameters);

        using JsonDocument document = JsonSerializer.Deserialize<JsonDocument>(json)!;

        document.RootElement.EnumerateObject().Select(member => member.Name).Should().BeEquivalentTo(
            "walk",
            "timeoutSeconds",
            "retries",
            "maxRepetitions",
            "maxRows",
            "maxNeighbors",
            "maxForwardingEntries");
    }

    [Fact]
    public void Result_Deserialises_WhatTheCollectorActuallySends()
    {
        // Written out as the collector's own payload() function writes it, rather than by
        // serialising the C# type and reading it back — which would only prove this file agrees
        // with itself.
        const string json = """
            {
              "walk": "clients",
              "neighborsSupported": true,
              "neighborCount": 2,
              "neighborsTruncated": false,
              "neighbors": [
                {"ipAddress": "10.10.0.21", "macAddress": "AA:BB:CC:00:00:21", "ifIndex": 10},
                {"ipAddress": "2001:db8::50", "macAddress": "AA:BB:CC:00:00:50", "ifIndex": 2}
              ],
              "forwardingSupported": true,
              "forwardingCount": 1,
              "forwardingTruncated": true,
              "forwarding": [
                {"macAddress": "AA:BB:CC:00:00:21", "ifIndex": 1, "vlanId": 10, "macCountOnPort": 1}
              ]
            }
            """;

        ClientWalkResult? result = JsonSerializer.Deserialize(
            json,
            ClientSerializerContext.Default.ClientWalkResult);

        result.Should().NotBeNull();
        result!.Walk.Should().Be("clients");
        result.NeighborsSupported.Should().BeTrue();
        result.NeighborCount.Should().Be(2);
        result.Neighbors.Should().HaveCount(2);
        result.Neighbors![1].IpAddress.Should().Be("2001:db8::50");
        result.ForwardingSupported.Should().BeTrue();
        result.ForwardingTruncated.Should().BeTrue();
        result.Forwarding.Should().ContainSingle();
        result.Forwarding![0].VlanId.Should().Be(10);
        result.Forwarding[0].MacCountOnPort.Should().Be(1);
    }

    [Fact]
    public void Result_FromADeviceThatImplementsNeitherTable_ReadsAsUnsupportedRatherThanEmpty()
    {
        // The distinction the interval tables depend on. A supported-and-empty reading lets the
        // handler conclude something; an unsupported one must let it conclude nothing.
        const string json = """
            {
              "walk": "clients",
              "neighborsSupported": false,
              "neighborCount": 0,
              "neighborsTruncated": false,
              "neighbors": [],
              "forwardingSupported": false,
              "forwardingCount": 0,
              "forwardingTruncated": false,
              "forwarding": []
            }
            """;

        ClientWalkResult result = JsonSerializer.Deserialize(
            json,
            ClientSerializerContext.Default.ClientWalkResult)!;

        result.NeighborsSupported.Should().BeFalse();
        result.ForwardingSupported.Should().BeFalse();
    }

    [Fact]
    public void Result_FromAnotherWalk_ReadsWithADiscriminatorThisPackageCanRefuse()
    {
        // Three walks share the Discover kind and sit in one table looking identical. The
        // handler filters on this member, so it has to survive deserialisation of a payload that
        // is not ours rather than throwing.
        ClientWalkResult result = JsonSerializer.Deserialize(
            """{"walk": "sweep", "responders": []}""",
            ClientSerializerContext.Default.ClientWalkResult)!;

        result.Walk.Should().Be("sweep");
        result.Neighbors.Should().BeNull();
    }
}
