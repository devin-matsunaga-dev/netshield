using System.Text.Json;

using FluentAssertions;

using NetShield.Inventory.Discovery;

namespace NetShield.UnitTests.Inventory;

/// <summary>
/// The two fingerprint payloads that cross between the API and <c>netshield-collector</c>.
/// </summary>
/// <remarks>
/// There is no generator between the C# shapes and the Python ones — the collector contract is
/// deliberately absent from the OpenAPI document (WP-1.3) — so the two sides are two hand-written
/// copies and the property names are the whole of the agreement between them. These tests pin
/// those names on this side; <c>tests/test_snmp_executor.py</c> pins the same names on the other.
/// A rename that breaks the agreement fails one gate or the other.
/// </remarks>
public sealed class DiscoveryPayloadTests
{
    private static readonly DiscoveryOptions Options = new()
    {
        RequestTimeoutSeconds = 5,
        Retries = 2,
        MaxRepetitions = 25,
        MaxRowsPerSubtree = 20_000,
        MaxInterfaces = 512
    };

    [Fact]
    public void Parameters_FromOptions_CarryTheWalkNameAndTheConfiguredValues()
    {
        SnmpWalkParameters parameters = SnmpWalkParameters.From(Options);

        parameters.Walk.Should().Be("snmp");
        parameters.TimeoutSeconds.Should().Be(5);
        parameters.Retries.Should().Be(2);
        parameters.MaxRepetitions.Should().Be(25);
        parameters.MaxRows.Should().Be(20_000);
        parameters.MaxInterfaces.Should().Be(512);
    }

    [Fact]
    public void Parameters_Serialized_UseTheNamesTheCollectorReads()
    {
        string json = JsonSerializer.Serialize(
            SnmpWalkParameters.From(Options),
            DiscoverySerializerContext.Default.SnmpWalkParameters);

        using JsonDocument document = JsonDocument.Parse(json);

        document.RootElement.EnumerateObject().Select(member => member.Name)
            .Should().BeEquivalentTo(
                "walk", "timeoutSeconds", "retries", "maxRepetitions", "maxRows", "maxInterfaces");
    }

    [Fact]
    public void Result_AsTheCollectorWritesIt_RoundTripsIntoTheShapeTheHandlerReads()
    {
        SnmpWalkResult? result = JsonSerializer.Deserialize(
            FromTheCollector,
            DiscoverySerializerContext.Default.SnmpWalkResult);

        result.Should().NotBeNull();
        result!.Walk.Should().Be("snmp");
        result.Vendor.Should().Be("CiscoIos");
        result.ReducedCapability.Should().BeFalse();
        result.SysObjectId.Should().Be("1.3.6.1.4.1.9.1.2494");
        result.SysName.Should().Be("lab-sw-ios-01");
        result.UptimeSeconds.Should().Be(1234567.89);
        result.Model.Should().Be("WS-C2960X-48FPD-L");
        result.OsVersion.Should().Be("15.2(7)E3");
        result.SerialNumber.Should().Be("FOC1234X5YZ");
        result.InterfaceCount.Should().Be(2);
        result.InterfacesTruncated.Should().BeFalse();

        result.Interfaces.Should().HaveCount(2);

        SnmpWalkInterface first = result.Interfaces![0];

        first.Index.Should().Be(1);
        first.Name.Should().Be("Gi0/1");
        first.Description.Should().Be("GigabitEthernet0/1");
        first.Alias.Should().Be("uplink to core");
        first.InterfaceType.Should().Be(6);
        first.Mtu.Should().Be(1500);
        first.SpeedBitsPerSecond.Should().Be(1_000_000_000);
        first.PhysicalAddress.Should().Be("00:1A:2B:3C:4D:01");
        first.AdminStatus.Should().Be(1);
        first.OperStatus.Should().Be(1);

        // The 10G port: ifSpeed saturated and the collector used ifHighSpeed instead, so the
        // value that arrives is wider than a 32-bit gauge could have carried.
        result.Interfaces[1].SpeedBitsPerSecond.Should().Be(10_000_000_000);
    }

    [Fact]
    public void Result_WithNoInterfaces_IsReadAsADeviceThatReportedNoneRatherThanAsAFailure()
    {
        SnmpWalkResult? result = JsonSerializer.Deserialize(
            """{"walk":"snmp","vendor":"GenericSnmp","reducedCapability":true,"interfaceCount":0}""",
            DiscoverySerializerContext.Default.SnmpWalkResult);

        result.Should().NotBeNull();
        result!.ReducedCapability.Should().BeTrue();
        result.Interfaces.Should().BeNull();
        result.SerialNumber.Should().BeNull();
    }

    /// <summary>
    /// What <c>collector/snmp/executor.py</c> produces for the <c>cisco_ios</c> fixture, member
    /// for member. If this stops deserialising, the two hand-written shapes have drifted.
    /// </summary>
    private const string FromTheCollector = """
        {
          "walk": "snmp",
          "vendor": "CiscoIos",
          "reducedCapability": false,
          "sysObjectId": "1.3.6.1.4.1.9.1.2494",
          "sysDescr": "Cisco IOS Software, C2960X Software (C2960X-UNIVERSALK9-M), Version 15.2(7)E3, RELEASE SOFTWARE (fc2)",
          "sysName": "lab-sw-ios-01",
          "sysContact": "netops@example.invalid",
          "sysLocation": "Lab rack 3",
          "uptimeSeconds": 1234567.89,
          "model": "WS-C2960X-48FPD-L",
          "osVersion": "15.2(7)E3",
          "serialNumber": "FOC1234X5YZ",
          "interfaceCount": 2,
          "interfacesTruncated": false,
          "interfaces": [
            {
              "index": 1,
              "name": "Gi0/1",
              "description": "GigabitEthernet0/1",
              "alias": "uplink to core",
              "interfaceType": 6,
              "mtu": 1500,
              "speedBitsPerSecond": 1000000000,
              "physicalAddress": "00:1A:2B:3C:4D:01",
              "adminStatus": 1,
              "operStatus": 1
            },
            {
              "index": 2,
              "name": "Te1/0/1",
              "description": "TenGigabitEthernet1/0/1",
              "alias": null,
              "interfaceType": 6,
              "mtu": 9216,
              "speedBitsPerSecond": 10000000000,
              "physicalAddress": "00:1A:2B:3C:4D:02",
              "adminStatus": 1,
              "operStatus": 2
            }
          ]
        }
        """;
}
