using FluentAssertions;

using NetShield.Contracts.Inventory;

using NetShield.Inventory.Topology;

using Facts = NetShield.Inventory.Topology.PortOccupancyRule.NeighborFacts;

namespace NetShield.UnitTests.Inventory;

/// <summary>
/// Access port or uplink, as arithmetic.
/// </summary>
/// <remarks>
/// "An uplink says how many addresses it carries rather than listing them as occupants" is the
/// WP-2.6 criterion this decides, and it is the one that would be actively misleading if it were
/// wrong: a MAC address is learned by every bridge on the path to it, so listing an uplink's
/// occupants would say two hundred hosts are plugged into one cable.
///
/// The order the reasons are considered in is the whole rule, which is why most of these tests
/// are about what does <em>not</em> get to overturn what.
/// </remarks>
public sealed class PortOccupancyRuleTests
{
    private const int Threshold = 8;

    private static readonly Facts ManagedSwitch = new(true, [SystemCapability.Bridge]);
    private static readonly Facts UnmanagedSwitch = new(false, [SystemCapability.Bridge]);
    private static readonly Facts AccessPoint =
        new(false, [SystemCapability.Bridge, SystemCapability.WlanAccessPoint]);
    private static readonly Facts Phone =
        new(false, [SystemCapability.Bridge, SystemCapability.Telephone]);
    private static readonly Facts Silent = new(false, []);

    // --- nothing on the port ---------------------------------------------------------------------

    [Fact]
    public void Classify_WithNothingObserved_IsEmpty()
    {
        PortOccupancyRule.Classify([], null, 0, Threshold)
            .Should().Be(Result(PortRole.Empty, PortRoleReason.NoEvidence, clientsListed: true));
    }

    [Fact]
    public void Classify_WithNothingObserved_StillListsItsNoClients()
    {
        // "Not listed" is a thing to say about a port that has something to withhold. An empty
        // port withholding nothing would read as a port hiding something.
        PortOccupancyRule.Classify([], null, 0, Threshold).ClientsListed.Should().BeTrue();
    }

    // --- one host: the first criterion -----------------------------------------------------------

    [Fact]
    public void Classify_WithOneHost_IsAnAccessPortThatListsIt()
    {
        PortOccupancyRule.Classify([], 1, 1, Threshold)
            .Should().Be(Result(PortRole.Access, PortRoleReason.Endpoints, clientsListed: true));
    }

    [Fact]
    public void Classify_WithAHandfulOfHostsBelowTheThreshold_IsStillAnAccessPort()
    {
        // A phone with a PC behind it, or a small hypervisor. Seven is under eight.
        PortOccupancyRule.Classify([], 7, 7, Threshold)
            .Should().Be(Result(PortRole.Access, PortRoleReason.Endpoints, clientsListed: true));
    }

    // --- the managed far end: the second criterion ------------------------------------------------

    [Fact]
    public void Classify_WithAManagedDeviceOnThePort_IsAnUplink()
    {
        PortOccupancyRule.Classify([ManagedSwitch], 1, 1, Threshold)
            .Should().Be(Result(PortRole.Uplink, PortRoleReason.ManagedDevice, clientsListed: false));
    }

    [Fact]
    public void Classify_WithAManagedDeviceAndNoAddressesAtAll_IsStillAnUplink()
    {
        // The topology said so, established from a protocol rather than from a count. A quiet
        // link between two switches is not an access port.
        PortOccupancyRule.Classify([ManagedSwitch], null, 0, Threshold)
            .Should().Be(Result(PortRole.Uplink, PortRoleReason.ManagedDevice, clientsListed: false));
    }

    [Fact]
    public void Classify_WithAManagedDevice_PrefersItOverEveryOtherReading()
    {
        // Two hundred addresses would also make this an uplink, and it must not be the reason:
        // the count is a configured threshold and the edge is a fact, and an operator reading
        // "learned address count" would go looking for the wrong thing.
        PortOccupancyRule.Classify([ManagedSwitch], 200, 200, Threshold)
            .Reason.Should().Be(PortRoleReason.ManagedDevice);
    }

    // --- the unmanaged far end ---------------------------------------------------------------------

    [Fact]
    public void Classify_WithAnUnmanagedSwitchOnThePort_IsAnUplink()
    {
        // NetShield does not monitor it, so there is no edge to another node — but everything
        // behind it still reaches the network through this port.
        PortOccupancyRule.Classify([UnmanagedSwitch], 3, 3, Threshold)
            .Should().Be(Result(
                PortRole.Uplink,
                PortRoleReason.InfrastructureNeighbor,
                clientsListed: false));
    }

    // --- the third criterion: an endpoint that bridges is still an endpoint -------------------------

    [Fact]
    public void Classify_WithAnAccessPointOnThePort_IsAnAccessPort()
    {
        // The WP-2.6 criterion. An AP advertises Bridge because bridging is what it does, and a
        // rule reading that bit alone would stop showing the access point on its own port.
        PortOccupancyRule.Classify([AccessPoint], 2, 2, Threshold)
            .Should().Be(Result(PortRole.Access, PortRoleReason.Endpoints, clientsListed: true));
    }

    [Fact]
    public void Classify_WithAPhoneOnThePort_IsAnAccessPort()
    {
        PortOccupancyRule.Classify([Phone], 2, 2, Threshold)
            .Should().Be(Result(PortRole.Access, PortRoleReason.Endpoints, clientsListed: true));
    }

    [Fact]
    public void Classify_WithAnAccessPointCarryingManyAddresses_IsAnUplinkOnTheCount()
    {
        // An AP does not stop being an AP, but a hundred addresses learned on that port are
        // wireless clients behind it rather than things plugged into it — so they are counted.
        PortOccupancyRule.Classify([AccessPoint], 100, 40, Threshold)
            .Should().Be(Result(
                PortRole.Uplink,
                PortRoleReason.LearnedAddressCount,
                clientsListed: false));
    }

    // --- the fourth criterion: the count -----------------------------------------------------------

    [Fact]
    public void Classify_WithAnAddressCountAtTheThreshold_IsAnUplink()
    {
        PortOccupancyRule.Classify([], Threshold, Threshold, Threshold)
            .Should().Be(Result(
                PortRole.Uplink,
                PortRoleReason.LearnedAddressCount,
                clientsListed: false));
    }

    [Fact]
    public void Classify_WithTwoHundredAddresses_IsAnUplinkAndListsNothing()
    {
        PortOccupancyRule.Classify([Silent], 200, 200, Threshold)
            .ClientsListed.Should().BeFalse();
    }

    [Fact]
    public void Classify_ReadsAConfiguredThresholdRatherThanAFixedOne()
    {
        // It is a classification threshold, not a protocol truth, and an estate where every desk
        // has a hypervisor should be able to move it.
        PortOccupancyRule.Classify([], 20, 20, uplinkAddressThreshold: 50)
            .Role.Should().Be(PortRole.Access);

        PortOccupancyRule.Classify([], 20, 20, uplinkAddressThreshold: 4)
            .Role.Should().Be(PortRole.Uplink);
    }

    // --- where the count comes from -----------------------------------------------------------------

    [Fact]
    public void Classify_WithNoCountFromTheDevice_FallsBackToTheBindingsItHolds()
    {
        // An agent answering only the VLAN-unaware forwarding database records no count. What
        // NetShield holds under-reports it — those are the addresses it resolved to clients —
        // so a port that reaches the threshold on this reading has certainly reached it on the
        // device's own.
        PortOccupancyRule.Classify([], null, 12, Threshold)
            .Should().Be(Result(
                PortRole.Uplink,
                PortRoleReason.LearnedAddressCount,
                clientsListed: false));
    }

    [Fact]
    public void Classify_PrefersTheDevicesOwnCountOverTheBindingsItHolds()
    {
        // The switch says two hundred; NetShield has resolved three of them to clients. The port
        // is an uplink on the switch's evidence, and listing the three would say those three are
        // what is plugged in.
        PortOccupancyRule.Classify([], 200, 3, Threshold)
            .Should().Be(Result(
                PortRole.Uplink,
                PortRoleReason.LearnedAddressCount,
                clientsListed: false));
    }

    [Fact]
    public void Classify_WithANeighborButNoAddresses_IsAnAccessPort()
    {
        // Something announced itself and nothing was learned behind it — a printer that speaks
        // LLDP and has not sent a frame since the switch last aged its table.
        PortOccupancyRule.Classify([Silent], null, 0, Threshold)
            .Should().Be(Result(PortRole.Access, PortRoleReason.Endpoints, clientsListed: true));
    }

    private static PortOccupancyRule.PortClassification Result(
        PortRole role,
        PortRoleReason reason,
        bool clientsListed) =>
        new(role, reason, clientsListed);
}
