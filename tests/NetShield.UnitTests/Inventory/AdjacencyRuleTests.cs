using FluentAssertions;

using NetShield.Contracts.Inventory;

using NetShield.Inventory.Topology;

namespace NetShield.UnitTests.Inventory;

/// <summary>
/// The two decisions WP-2.1 is measured on, tested as arithmetic.
/// </summary>
/// <remarks>
/// "Conflicting LLDP/CDP data resolves deterministically" and "edge confidence when sources
/// disagree" are the package's criteria, and everything around them — the tables, the applier,
/// the schedule — is machinery an integration test exercises end to end. These are the decisions
/// themselves, and they read nothing, wait for nothing and depend on nothing.
/// </remarks>
public sealed class AdjacencyRuleTests
{
    private const string Core = "device:core";
    private const string Ghost = "chassis:deviceid:ghost-sw-7|port:gi1/1";

    // --- Survivors: which accounts of one port become edges ------------------------------------

    [Fact]
    public void Survivors_WithOneAccountOfAPort_KeepsIt()
    {
        Survivors(Lldp(Core)).Should().BeEquivalentTo([Core]);
    }

    [Fact]
    public void Survivors_WithBothProtocolsAgreeing_KeepsTheOneEdgeTheyShare()
    {
        // They agree because both resolved to the same device, which is what gives them one key.
        // The merge is the point: one edge carrying two sources, not two edges.
        Survivors(Lldp(Core), Cdp(Core)).Should().BeEquivalentTo([Core]);
    }

    [Fact]
    public void Survivors_WhenLldpAndCdpNameDifferentFarEnds_KeepsTheLldpOne()
    {
        // The WP-2.1 criterion. An LLDP chassis identifier is an identity; a CDP device id is
        // usually a host name, and WP-1.1 settled that a host name is not one.
        Survivors(Lldp(Core), Cdp(Ghost)).Should().BeEquivalentTo([Core]);
    }

    [Fact]
    public void Survivors_WhenOnlyCdpReportsThePort_KeepsIt()
    {
        // CDP does not lose to LLDP in general. It loses to LLDP *on the same port*, and a port
        // LLDP says nothing about has no argument to lose.
        Survivors(Cdp(Ghost)).Should().BeEquivalentTo([Ghost]);
    }

    [Fact]
    public void Survivors_KeepsARoutingAccountEvenWhereLldpDisagrees()
    {
        // Layer 3 is answering a different question and is not in the argument: a gateway one hop
        // away is a real observation whatever LLDP says about the cable.
        Survivors(Lldp(Core), Routing("device:gateway"))
            .Should().BeEquivalentTo([Core, "device:gateway"]);
    }

    [Fact]
    public void Survivors_KeepsAGroupThatCdpAndRoutingBothSupport()
    {
        Survivors(Lldp(Core), Cdp(Ghost), Routing(Ghost)).Should().BeEquivalentTo([Core, Ghost]);
    }

    [Fact]
    public void Survivors_DoesNotDependOnTheOrderTheAccountsArrivedIn()
    {
        IReadOnlySet<string> forwards = Survivors(Lldp(Core), Cdp(Ghost), Routing("device:gw"));
        IReadOnlySet<string> backwards = Survivors(Routing("device:gw"), Cdp(Ghost), Lldp(Core));

        forwards.Should().BeEquivalentTo(backwards);
    }

    [Fact]
    public void Survivors_WithNothingOnThePort_IsEmpty()
    {
        AdjacencyRule.Survivors([]).Should().BeEmpty();
    }

    [Fact]
    public void Survivors_KeepsTwoLldpNeighboursOnOnePort()
    {
        // A hub, or a phone with a workstation behind it. Both are true and neither suppresses
        // the other — the rule is about protocols disagreeing, not about a port having one
        // neighbour.
        Survivors(Lldp("chassis:macaddress:aa|port:1"), Lldp("chassis:macaddress:bb|port:1"))
            .Should().HaveCount(2);
    }

    // --- Confidence ----------------------------------------------------------------------------

    [Fact]
    public void Confidence_WithANeighbourProtocolFromBothEnds_IsConfirmed()
    {
        AdjacencyRule.Confidence([NeighborSource.Lldp], bidirectional: true)
            .Should().Be(AdjacencyConfidence.Confirmed);
    }

    [Fact]
    public void Confidence_WithANeighbourProtocolFromOneEnd_IsProbable()
    {
        AdjacencyRule.Confidence([NeighborSource.Lldp], bidirectional: false)
            .Should().Be(AdjacencyConfidence.Probable);
    }

    [Fact]
    public void Confidence_WithCdpAloneFromOneEnd_IsProbable()
    {
        AdjacencyRule.Confidence([NeighborSource.Cdp], bidirectional: false)
            .Should().Be(AdjacencyConfidence.Probable);
    }

    [Fact]
    public void Confidence_WithRoutingAloneFromOneEnd_IsPossible()
    {
        // A next hop says two devices are one hop apart at layer 3, which need not be one cable.
        AdjacencyRule.Confidence([NeighborSource.Routing], bidirectional: false)
            .Should().Be(AdjacencyConfidence.Possible);
    }

    [Fact]
    public void Confidence_WithRoutingFromBothEnds_IsProbable()
    {
        // Two routers each routing through the other is a real mutual observation, and it is
        // still about layer 3 — so it climbs one rung and not two.
        AdjacencyRule.Confidence([NeighborSource.Routing], bidirectional: true)
            .Should().Be(AdjacencyConfidence.Probable);
    }

    [Fact]
    public void Confidence_WithRoutingBesideANeighbourProtocol_ReadsTheNeighbourProtocol()
    {
        AdjacencyRule.Confidence([NeighborSource.Routing, NeighborSource.Cdp], bidirectional: true)
            .Should().Be(AdjacencyConfidence.Confirmed);
    }

    [Fact]
    public void Confidence_WithNoSourcesAtAll_IsPossible()
    {
        // Not reachable through the applier — an edge exists because something observed it — but
        // the weakest answer is the right one for a set that claims nothing.
        AdjacencyRule.Confidence([], bidirectional: false).Should().Be(AdjacencyConfidence.Possible);
    }

    // --- Canonical ordering ---------------------------------------------------------------------

    [Fact]
    public void ShouldSwap_WhenTheFarDeviceSortsLower_Swaps()
    {
        AdjacencyRule.ShouldSwap(Guid.Parse("22222222-0000-0000-0000-000000000000"), 1,
            Guid.Parse("11111111-0000-0000-0000-000000000000"), 1)
            .Should().BeTrue();
    }

    [Fact]
    public void ShouldSwap_WhenTheLocalDeviceSortsLower_DoesNot()
    {
        AdjacencyRule.ShouldSwap(Guid.Parse("11111111-0000-0000-0000-000000000000"), 1,
            Guid.Parse("22222222-0000-0000-0000-000000000000"), 1)
            .Should().BeFalse();
    }

    [Fact]
    public void ShouldSwap_OnOneDeviceCabledToItself_OrdersByInterface()
    {
        Guid device = Guid.Parse("33333333-0000-0000-0000-000000000000");

        AdjacencyRule.ShouldSwap(device, 4, device, 2).Should().BeTrue();
        AdjacencyRule.ShouldSwap(device, 2, device, 4).Should().BeFalse();
    }

    [Fact]
    public void ShouldSwap_WhenTheFarEndIsNotADevice_DoesNot()
    {
        AdjacencyRule.ShouldSwap(Guid.NewGuid(), 1, null, null).Should().BeFalse();
    }

    [Fact]
    public void ShouldSwap_WhenTheFarInterfaceIsUnresolved_DoesNot()
    {
        // Canonicalising on a device id alone would collapse a two-cable aggregate into one edge:
        // NetShield has not established that the two accounts are of the same link.
        AdjacencyRule.ShouldSwap(
            Guid.Parse("22222222-0000-0000-0000-000000000000"),
            1,
            Guid.Parse("11111111-0000-0000-0000-000000000000"),
            null)
            .Should().BeFalse();
    }

    [Fact]
    public void ShouldSwap_AgreesFromBothEndsOfOneLink()
    {
        // The property that makes an edge one row rather than two: exactly one of the two
        // devices swaps, whichever order they are asked in.
        Guid first = Guid.Parse("11111111-0000-0000-0000-000000000000");
        Guid second = Guid.Parse("22222222-0000-0000-0000-000000000000");

        bool fromFirst = AdjacencyRule.ShouldSwap(first, 3, second, 7);
        bool fromSecond = AdjacencyRule.ShouldSwap(second, 7, first, 3);

        fromFirst.Should().NotBe(fromSecond);
    }

    private static IReadOnlySet<string> Survivors(params AdjacencyRule.Candidate[] candidates) =>
        AdjacencyRule.Survivors(candidates);

    private static AdjacencyRule.Candidate Lldp(string key) =>
        new(NeighborSource.Lldp, key, key.StartsWith("device:", StringComparison.Ordinal));

    private static AdjacencyRule.Candidate Cdp(string key) =>
        new(NeighborSource.Cdp, key, key.StartsWith("device:", StringComparison.Ordinal));

    private static AdjacencyRule.Candidate Routing(string key) =>
        new(NeighborSource.Routing, key, key.StartsWith("device:", StringComparison.Ordinal));
}
