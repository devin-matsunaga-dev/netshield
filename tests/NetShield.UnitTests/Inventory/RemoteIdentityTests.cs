using FluentAssertions;

using NetShield.Contracts.Inventory;

using NetShield.Inventory.Topology;

namespace NetShield.UnitTests.Inventory;

/// <summary>
/// The two keys a far end is named by, and the difference between them.
/// </summary>
/// <remarks>
/// Confusing the raw identity with the adjacency key is the mistake <see cref="RemoteIdentity"/>
/// exists to prevent: one keeps two protocols' accounts apart as observations, and the other is
/// what merges them into one edge once both have been resolved to the same device.
/// </remarks>
public sealed class RemoteIdentityTests
{
    [Fact]
    public void Raw_NormalisesAMacAddressHoweverItWasSpelled()
    {
        // A Cisco prints aabb.ccdd.eeff, an SNMP octet string decodes to AA:BB:CC:DD:EE:FF, and
        // an operator types hyphens. All three are one far end.
        string dotted = RemoteIdentity.Raw(NeighborIdKind.MacAddress, "aabb.ccdd.eeff", "Et1");
        string colons = RemoteIdentity.Raw(NeighborIdKind.MacAddress, "AA:BB:CC:DD:EE:FF", "Et1");
        string hyphens = RemoteIdentity.Raw(NeighborIdKind.MacAddress, "aa-bb-cc-dd-ee-ff", "Et1");

        dotted.Should().Be(colons).And.Be(hyphens);
    }

    [Fact]
    public void Raw_FoldsCaseOnEverythingElse()
    {
        RemoteIdentity.Raw(NeighborIdKind.DeviceId, "Core-SW-1", "Gi0/1")
            .Should().Be(RemoteIdentity.Raw(NeighborIdKind.DeviceId, "core-sw-1", "gi0/1"));
    }

    [Fact]
    public void Raw_KeepsTwoIdentifiersOfDifferentKindsApart()
    {
        // The same twelve characters as a MAC and as a local(7) label are two different claims,
        // and only one of them can be matched against an inventoried interface.
        RemoteIdentity.Raw(NeighborIdKind.MacAddress, "AA:BB:CC:DD:EE:FF", null)
            .Should().NotBe(RemoteIdentity.Raw(NeighborIdKind.Local, "AA:BB:CC:DD:EE:FF", null));
    }

    [Fact]
    public void Raw_KeepsTwoNeighboursOnOnePortApart()
    {
        // A phone with a workstation behind it is the ordinary case, and a key without the port
        // would record one of them and withdraw the other on every walk.
        RemoteIdentity.Raw(NeighborIdKind.MacAddress, "AA:BB:CC:DD:EE:FF", "Et1")
            .Should().NotBe(RemoteIdentity.Raw(NeighborIdKind.MacAddress, "AA:BB:CC:DD:EE:FF", "Et2"));
    }

    [Fact]
    public void Raw_TreatsAnAbsentPortAsItsOwnPosition()
    {
        RemoteIdentity.Raw(NeighborIdKind.MacAddress, "AA:BB:CC:DD:EE:FF", null)
            .Should().EndWith("|port:");
    }

    [Fact]
    public void Raw_LeavesAMalformedMacAsItsOwnText()
    {
        // Eleven hex digits is a corrupted address rather than one worth guessing at, so it is
        // folded like any other string and stays distinguishable from a real one.
        RemoteIdentity.Raw(NeighborIdKind.MacAddress, "AA:BB:CC:DD:EE:F", null)
            .Should().Contain("aa:bb:cc:dd:ee:f");
    }

    [Fact]
    public void Adjacency_OfAResolvedFarEnd_IsTheDevice()
    {
        Guid device = Guid.NewGuid();

        RemoteIdentity.Adjacency(device, "chassis:macaddress:aa|port:et1")
            .Should().Be($"device:{device}");
    }

    [Fact]
    public void Adjacency_MergesTwoProtocolsThatResolvedToOneDevice()
    {
        // The whole reason the two keys are different keys. LLDP names a chassis address and CDP
        // names a host name; the raw keys differ and must, and the adjacency keys are equal so
        // the two accounts become one edge carrying both sources.
        Guid device = Guid.NewGuid();

        string fromLldp = RemoteIdentity.Adjacency(
            device,
            RemoteIdentity.Raw(NeighborIdKind.MacAddress, "00:1C:73:00:00:01", "Et1"));

        string fromCdp = RemoteIdentity.Adjacency(
            device,
            RemoteIdentity.Raw(NeighborIdKind.DeviceId, "core-sw-1", "Ethernet1"));

        fromLldp.Should().Be(fromCdp);
    }

    [Fact]
    public void Adjacency_OfAnUnresolvedFarEnd_IsTheRawIdentity()
    {
        // Two accounts of a stranger stay two edges: nothing has established they are the same
        // stranger, and asserting it would be a claim rather than an observation.
        string raw = RemoteIdentity.Raw(NeighborIdKind.DeviceId, "ghost-sw-7", "Gi1/1");

        RemoteIdentity.Adjacency(null, raw).Should().Be(raw);
    }

    [Fact]
    public void Normalize_OfAMacAddress_IsTheStoredSpelling()
    {
        // The same form device_interfaces.physical_address holds, which is what lets a chassis id
        // be matched against the interface inventory at all.
        RemoteIdentity.Normalize(NeighborIdKind.MacAddress, "aabb.ccdd.eeff")
            .Should().Be("AA:BB:CC:DD:EE:FF");
    }
}
