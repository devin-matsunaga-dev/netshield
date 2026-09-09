using FluentAssertions;

using NetShield.Contracts.Inventory;

using NetShield.Inventory.Topology;

namespace NetShield.UnitTests.Inventory;

/// <summary>
/// The capability map, in words. Two protocols, two bit tables, one vocabulary.
/// </summary>
/// <remarks>
/// "A port with an LLDP-speaking access point shows its name and its capability in words" is a
/// WP-2.6 criterion, and this is the half of it that is arithmetic. The bits themselves are
/// normalised by the collector — <c>collector.snmp.octets</c> delivers a mask whose bit
/// <c>N</c> is that protocol's bit <c>N</c> — so what is tested here is what the bits
/// <em>mean</em>, which is where LLDP and CDP part company.
/// </remarks>
public sealed class SystemCapabilitiesTests
{
    // IEEE 802.1AB LldpSystemCapabilitiesMap, by bit.
    private const int Other = 1 << 0;
    private const int Repeater = 1 << 1;
    private const int Bridge = 1 << 2;
    private const int WlanAccessPoint = 1 << 3;
    private const int Router = 1 << 4;
    private const int Telephone = 1 << 5;
    private const int Docsis = 1 << 6;
    private const int StationOnly = 1 << 7;

    // CISCO-CDP-MIB's capability word, by bit.
    private const int CdpRouter = 1 << 0;
    private const int CdpTransparentBridge = 1 << 1;
    private const int CdpSourceRouteBridge = 1 << 2;
    private const int CdpSwitch = 1 << 3;
    private const int CdpHost = 1 << 4;
    private const int CdpIgmp = 1 << 5;
    private const int CdpRepeater = 1 << 6;

    // --- LLDP ----------------------------------------------------------------------------------

    [Fact]
    public void Decode_OfAnLldpAccessPoint_SaysBridgeAndAccessPoint()
    {
        SystemCapabilities.Decode(NeighborSource.Lldp, Bridge | WlanAccessPoint)
            .Should().Equal(SystemCapability.Bridge, SystemCapability.WlanAccessPoint);
    }

    [Fact]
    public void Decode_OfAnLldpPhone_SaysBridgeAndTelephone()
    {
        SystemCapabilities.Decode(NeighborSource.Lldp, Bridge | Telephone)
            .Should().Equal(SystemCapability.Bridge, SystemCapability.Telephone);
    }

    [Fact]
    public void Decode_OfAnLldpCoreSwitch_SaysBridgeAndRouter()
    {
        SystemCapabilities.Decode(NeighborSource.Lldp, Bridge | Router)
            .Should().Equal(SystemCapability.Bridge, SystemCapability.Router);
    }

    [Fact]
    public void Decode_OfEveryLldpBit_NamesEveryCapabilityInDeclarationOrder()
    {
        int all = Other | Repeater | Bridge | WlanAccessPoint | Router | Telephone | Docsis | StationOnly;

        SystemCapabilities.Decode(NeighborSource.Lldp, all).Should().Equal(
            SystemCapability.Other,
            SystemCapability.Repeater,
            SystemCapability.Bridge,
            SystemCapability.WlanAccessPoint,
            SystemCapability.Router,
            SystemCapability.Telephone,
            SystemCapability.DocsisCableDevice,
            SystemCapability.Station);
    }

    [Fact]
    public void Decode_IgnoresABitAboveTheEightTheStandardDefines()
    {
        // The map is two octets wide and only the first eight bits are named. A ninth being set
        // is a device saying something this vocabulary has no word for, not a reason to fail.
        SystemCapabilities.Decode(NeighborSource.Lldp, Bridge | (1 << 9))
            .Should().Equal(SystemCapability.Bridge);
    }

    // --- CDP -----------------------------------------------------------------------------------

    [Fact]
    public void Decode_OfACdpSwitch_SaysBridge()
    {
        // Bit 3 in CDP. Bit 3 in LLDP is a wireless access point, which is the whole reason these
        // are two tables rather than one.
        SystemCapabilities.Decode(NeighborSource.Cdp, CdpSwitch)
            .Should().Equal(SystemCapability.Bridge);
    }

    [Fact]
    public void Decode_OfACdpRouter_SaysRouter()
    {
        SystemCapabilities.Decode(NeighborSource.Cdp, CdpRouter).Should().Equal(SystemCapability.Router);
    }

    [Fact]
    public void Decode_OfACdpHost_SaysStation()
    {
        SystemCapabilities.Decode(NeighborSource.Cdp, CdpHost).Should().Equal(SystemCapability.Station);
    }

    [Fact]
    public void Decode_OfACdpSourceRouteBridge_SaysBridge()
    {
        SystemCapabilities.Decode(NeighborSource.Cdp, CdpSourceRouteBridge)
            .Should().Equal(SystemCapability.Bridge);
    }

    [Fact]
    public void Decode_OfCdpIgmp_SaysNothing()
    {
        // A protocol feature is not a kind of device, and putting it in a list of what something
        // *is* would read as a fifth identity beside router, bridge, host and repeater.
        SystemCapabilities.Decode(NeighborSource.Cdp, CdpIgmp).Should().BeEmpty();
    }

    [Fact]
    public void Decode_OfACdpSwitchAndBridge_SaysBridgeOnce()
    {
        SystemCapabilities.Decode(NeighborSource.Cdp, CdpSwitch | CdpTransparentBridge)
            .Should().Equal(SystemCapability.Bridge);
    }

    [Fact]
    public void Decode_OfACdpRepeater_SaysRepeater()
    {
        SystemCapabilities.Decode(NeighborSource.Cdp, CdpRepeater)
            .Should().Equal(SystemCapability.Repeater);
    }

    [Fact]
    public void Decode_OfOneMaskReadTwoWays_DisagreesAndThatIsThePoint()
    {
        // Bits 2 and 4. As LLDP that is a bridge and a router — a core switch. As CDP it is a
        // source-route bridge and a host — a workstation. Applying one table to the other's
        // column names the wrong thing rather than failing, which is why the source picks the
        // table and there is no default.
        int mask = (1 << 2) | (1 << 4);

        SystemCapabilities.Decode(NeighborSource.Lldp, mask)
            .Should().Equal(SystemCapability.Bridge, SystemCapability.Router);

        SystemCapabilities.Decode(NeighborSource.Cdp, mask)
            .Should().Equal(SystemCapability.Bridge, SystemCapability.Station);
    }

    [Fact]
    public void Decode_ReadsTheMaskTheCollectorNormalisedRatherThanTheWireOctets()
    {
        // A bridge-and-router neighbour advertises the octets `28:00`, and `BITS` numbers from
        // the most significant bit — so the collector delivers bits 2 and 4, which is 0x14. The
        // wire spelling never reaches here, and reading 0x28 as the mask would say wireless
        // access point and telephone.
        SystemCapabilities.Decode(NeighborSource.Lldp, 0x14)
            .Should().Equal(SystemCapability.Bridge, SystemCapability.Router);

        SystemCapabilities.Decode(NeighborSource.Lldp, 0x28)
            .Should().Equal(SystemCapability.WlanAccessPoint, SystemCapability.Telephone);
    }

    // --- absence -------------------------------------------------------------------------------

    [Fact]
    public void Decode_OfNothing_SaysNothing()
    {
        SystemCapabilities.Decode(NeighborSource.Lldp, null).Should().BeEmpty();
        SystemCapabilities.Decode(NeighborSource.Lldp, 0).Should().BeEmpty();
    }

    [Fact]
    public void Decode_OfARoutingObservation_SaysNothingWhateverTheBitsAre()
    {
        // A next hop is a statement about layer 3. It says a gateway is reachable, not what the
        // gateway is, and there is no capability column on a routing table to read anyway.
        SystemCapabilities.Decode(NeighborSource.Routing, Bridge | Router).Should().BeEmpty();
    }

    // --- merging -------------------------------------------------------------------------------

    [Fact]
    public void Merge_OfTwoProtocolsDescribingOneFarEnd_UnionsThemThroughTheirOwnTables()
    {
        // One edge, supported by an LLDP account and a CDP account of the same switch. Two
        // witnesses, not two switches — and each read with its own table on the way in.
        SystemCapabilities.Merge([
            (NeighborSource.Lldp, Bridge),
            (NeighborSource.Cdp, CdpRouter)
        ]).Should().Equal(SystemCapability.Bridge, SystemCapability.Router);
    }

    [Fact]
    public void Merge_OfNoObservations_SaysNothing()
    {
        SystemCapabilities.Merge([]).Should().BeEmpty();
    }

    // --- what counts as infrastructure ---------------------------------------------------------

    [Fact]
    public void IsInfrastructure_OfABridge_IsTrue()
    {
        SystemCapabilities.IsInfrastructure([SystemCapability.Bridge]).Should().BeTrue();
    }

    [Fact]
    public void IsInfrastructure_OfARouter_IsTrue()
    {
        SystemCapabilities.IsInfrastructure([SystemCapability.Router]).Should().BeTrue();
    }

    [Fact]
    public void IsInfrastructure_OfAnAccessPoint_IsFalse()
    {
        // An access point bridges, and says so — but it is the endpoint an operator means when
        // they ask what is on that port, and its wireless clients are never learned on this
        // switch's port in the first place.
        SystemCapabilities.IsInfrastructure([SystemCapability.Bridge, SystemCapability.WlanAccessPoint])
            .Should().BeFalse();
    }

    [Fact]
    public void IsInfrastructure_OfAPhone_IsFalse()
    {
        // A desk phone advertises Bridge because it has a switch in it for the PC behind it.
        // Reading the bridge bit alone would make every phone port an uplink and stop it showing
        // the phone, which is the question the screen exists to answer.
        SystemCapabilities.IsInfrastructure([SystemCapability.Bridge, SystemCapability.Telephone])
            .Should().BeFalse();
    }

    [Fact]
    public void IsInfrastructure_OfABridgingHost_IsFalse_AndFallsToTheAddressCount()
    {
        // A hypervisor. It is an endpoint by this reading, and PortOccupancyRule then decides on
        // how many addresses are actually behind it — which is evidence rather than a bit.
        SystemCapabilities.IsInfrastructure([SystemCapability.Bridge, SystemCapability.Station])
            .Should().BeFalse();
    }

    [Fact]
    public void IsInfrastructure_OfAStation_IsFalse()
    {
        SystemCapabilities.IsInfrastructure([SystemCapability.Station]).Should().BeFalse();
    }

    [Fact]
    public void IsInfrastructure_OfNothing_IsFalse()
    {
        SystemCapabilities.IsInfrastructure([]).Should().BeFalse();
    }
}
