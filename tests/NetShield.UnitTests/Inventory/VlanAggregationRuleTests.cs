using FluentAssertions;

using NetShield.Inventory.Topology;

namespace NetShield.UnitTests.Inventory;

/// <summary>
/// The WP-2.2 merge as arithmetic: what several switches carrying one VLAN add up to.
/// </summary>
/// <remarks>
/// <em>"A VLAN present on multiple switches appears once with aggregated membership"</em> is the
/// criterion, and this is where it is proved as a function rather than through a database. The
/// integration suite proves the same thing end to end; this proves it is deterministic, which a
/// round trip through PostgreSQL cannot.
/// </remarks>
public sealed class VlanAggregationRuleTests
{
    private static readonly DateTimeOffset Monday =
        new(2026, 9, 7, 9, 0, 0, TimeSpan.Zero);

    private static VlanAggregationRule.Membership Membership(
        string? name,
        int portCount = 1,
        int firstDay = 0,
        int lastDay = 0,
        Guid? deviceId = null) =>
        new(
            deviceId ?? Guid.NewGuid(),
            name,
            portCount,
            Monday.AddDays(firstDay),
            Monday.AddDays(lastDay));

    // --- one VLAN, several switches ---------------------------------------------------------

    [Fact]
    public void Aggregate_WithOneMembership_IsThatMembership()
    {
        VlanAggregationRule.Aggregated aggregated =
            VlanAggregationRule.Aggregate([Membership("Users VLAN", portCount: 4)]);

        aggregated.Name.Should().Be("Users VLAN");
        aggregated.NameDisputed.Should().BeFalse();
        aggregated.DeviceCount.Should().Be(1);
        aggregated.PortCount.Should().Be(4);
    }

    [Fact]
    public void Aggregate_WithThreeSwitches_CountsThemOnce()
    {
        VlanAggregationRule.Aggregated aggregated = VlanAggregationRule.Aggregate(
        [
            Membership("Users VLAN", portCount: 4),
            Membership("Users VLAN", portCount: 2),
            Membership("Users VLAN", portCount: 1)
        ]);

        aggregated.DeviceCount.Should().Be(3);
    }

    [Fact]
    public void Aggregate_SumsMembershipRatherThanUnioningIt()
    {
        // A port belongs to exactly one device, so there is nothing to de-duplicate — which is
        // the opposite of WP-2.1's edges, where both ends describe one cable.
        VlanAggregationRule.Aggregated aggregated = VlanAggregationRule.Aggregate(
        [
            Membership("Server VLAN", portCount: 4),
            Membership("Server VLAN", portCount: 4)
        ]);

        aggregated.PortCount.Should().Be(8);
    }

    [Fact]
    public void Aggregate_TakesTheEarliestDiscoveryAndTheLatestSighting()
    {
        VlanAggregationRule.Aggregated aggregated = VlanAggregationRule.Aggregate(
        [
            Membership("Voice VLAN", firstDay: 3, lastDay: 5),
            Membership("Voice VLAN", firstDay: 1, lastDay: 4)
        ]);

        aggregated.FirstDiscoveredAt.Should().Be(Monday.AddDays(1));
        aggregated.LastSeenAt.Should().Be(Monday.AddDays(5));
    }

    [Fact]
    public void Aggregate_CountsOneDeviceOnceHoweverManyTimesItAppears()
    {
        Guid core = Guid.NewGuid();

        VlanAggregationRule.Aggregated aggregated = VlanAggregationRule.Aggregate(
        [
            Membership("Users VLAN", deviceId: core),
            Membership("Users VLAN", deviceId: core)
        ]);

        aggregated.DeviceCount.Should().Be(1);
    }

    // --- the name ----------------------------------------------------------------------------

    [Fact]
    public void Aggregate_WithEveryDeviceAgreeing_DoesNotReportADispute()
    {
        VlanAggregationRule.Aggregated aggregated = VlanAggregationRule.Aggregate(
        [
            Membership("Guest VLAN"),
            Membership("Guest VLAN"),
            Membership("Guest VLAN")
        ]);

        aggregated.Name.Should().Be("Guest VLAN");
        aggregated.NameDisputed.Should().BeFalse();
        aggregated.Names.Should().ContainSingle();
    }

    [Fact]
    public void Aggregate_WithTwoSpellings_TakesTheOneMoreDevicesUse()
    {
        VlanAggregationRule.Aggregated aggregated = VlanAggregationRule.Aggregate(
        [
            Membership("Users VLAN"),
            Membership("Users VLAN"),
            Membership("Staff")
        ]);

        aggregated.Name.Should().Be("Users VLAN");
        aggregated.NameDisputed.Should().BeTrue();
        aggregated.Names.Should().Equal("Users VLAN", "Staff");
    }

    [Fact]
    public void Aggregate_WithSpellingsUsedEquallyOften_TakesTheAlphabeticallyFirst()
    {
        // Deterministic in the mathematical sense: a tie has to be broken by something that is a
        // property of the values rather than of the order they arrived in.
        VlanAggregationRule.Aggregated aggregated =
            VlanAggregationRule.Aggregate([Membership("Users VLAN"), Membership("Staff")]);

        aggregated.Name.Should().Be("Staff");
    }

    [Fact]
    public void Aggregate_DoesNotDependOnTheOrderTheMembershipsArriveIn()
    {
        VlanAggregationRule.Membership[] memberships =
        [
            Membership("Users VLAN", portCount: 2),
            Membership("Staff", portCount: 1),
            Membership("Users VLAN", portCount: 3)
        ];

        VlanAggregationRule.Aggregated forwards = VlanAggregationRule.Aggregate(memberships);
        VlanAggregationRule.Aggregated backwards =
            VlanAggregationRule.Aggregate([.. memberships.Reverse()]);

        backwards.Should().BeEquivalentTo(forwards);
    }

    [Fact]
    public void Aggregate_TreatsADifferenceOfCaseAsAgreement()
    {
        // One operator typing the same word twice. Flagging that would make the flag mean nothing
        // on a real estate.
        VlanAggregationRule.Aggregated aggregated =
            VlanAggregationRule.Aggregate([Membership("Users VLAN"), Membership("USERS VLAN")]);

        aggregated.NameDisputed.Should().BeFalse();
        aggregated.Names.Should().ContainSingle();
    }

    [Fact]
    public void Aggregate_ReportsASpellingSomebodyActuallyTyped()
    {
        VlanAggregationRule.Aggregated aggregated = VlanAggregationRule.Aggregate(
        [
            Membership("USERS VLAN"),
            Membership("USERS VLAN"),
            Membership("Users VLAN")
        ]);

        aggregated.Name.Should().Be("USERS VLAN");
    }

    [Fact]
    public void Aggregate_IgnoresLeadingAndTrailingWhitespace()
    {
        VlanAggregationRule.Aggregated aggregated =
            VlanAggregationRule.Aggregate([Membership("Voice VLAN"), Membership("  Voice VLAN  ")]);

        aggregated.Name.Should().Be("Voice VLAN");
        aggregated.NameDisputed.Should().BeFalse();
    }

    [Fact]
    public void Aggregate_WithADeviceThatNamedNothing_DoesNotLetItBlankTheName()
    {
        // A switch answering the nameless fallback table is not a vote for anonymity. It simply
        // does not vote.
        VlanAggregationRule.Aggregated aggregated =
            VlanAggregationRule.Aggregate([Membership("IoT VLAN"), Membership(null)]);

        aggregated.Name.Should().Be("IoT VLAN");
        aggregated.NameDisputed.Should().BeFalse();
        aggregated.DeviceCount.Should().Be(2);
    }

    [Fact]
    public void Aggregate_WithNoDeviceNamingIt_HasNoName()
    {
        VlanAggregationRule.Aggregated aggregated =
            VlanAggregationRule.Aggregate([Membership(null), Membership("   ")]);

        aggregated.Name.Should().BeNull();
        aggregated.NameDisputed.Should().BeFalse();
        aggregated.Names.Should().BeEmpty();
        aggregated.DeviceCount.Should().Be(2);
    }

    // --- the refusal -------------------------------------------------------------------------

    [Fact]
    public void Aggregate_WithNoMemberships_Throws()
    {
        // A VLAN exists in NetShield because a device reported it. There is no such thing as one
        // with no memberships, and answering with an empty row would put a VLAN nothing carries
        // into the inventory.
        Action aggregating = () => VlanAggregationRule.Aggregate([]);

        aggregating.Should().Throw<ArgumentException>();
    }
}
