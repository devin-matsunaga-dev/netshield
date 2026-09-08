using FluentAssertions;

using NetShield.Contracts.Inventory;

using NetShield.Inventory.Discovery;

namespace NetShield.UnitTests.Inventory;

/// <summary>
/// The IF-MIB integers a walk recorded become named statuses at the module boundary.
/// </summary>
/// <remarks>
/// <para>
/// The row keeps the raw integer — a diagnostic wants to know that a device answered <c>9</c>,
/// not that NetShield gave up on it — and the contract carries the name, so the wire and the
/// screen speak about interfaces rather than about SNMP.
/// </para>
/// <para>
/// The two counters do not share a vocabulary: <c>ifAdminStatus</c> defines three values and
/// <c>ifOperStatus</c> seven, and <c>5</c> means <c>dormant</c> on one and nothing at all on the
/// other. Mapping them through one table would quietly invent an administrative intent no device
/// expressed.
/// </para>
/// </remarks>
public sealed class InterfaceStatusMappingTests
{
    [Theory]
    [InlineData(1, InterfaceStatus.Up)]
    [InlineData(2, InterfaceStatus.Down)]
    [InlineData(3, InterfaceStatus.Testing)]
    public void AdminStatus_TheThreeValuesTheMibDefines_AreNamed(int raw, InterfaceStatus expected)
    {
        Map(adminStatus: raw).AdminStatus.Should().Be(expected);
    }

    [Theory]
    [InlineData(1, InterfaceStatus.Up)]
    [InlineData(2, InterfaceStatus.Down)]
    [InlineData(3, InterfaceStatus.Testing)]
    [InlineData(5, InterfaceStatus.Dormant)]
    [InlineData(6, InterfaceStatus.NotPresent)]
    [InlineData(7, InterfaceStatus.LowerLayerDown)]
    public void OperStatus_TheValuesTheMibDefines_AreNamed(int raw, InterfaceStatus expected)
    {
        Map(operStatus: raw).OperStatus.Should().Be(expected);
    }

    /// <summary>
    /// <c>ifOperStatus</c> 4 is <c>unknown</c>. It lands on the same member an unrecognised value
    /// does, because "the device does not know" and "the device said something nobody can read"
    /// are not distinctions a screen can act on differently.
    /// </summary>
    [Theory]
    [InlineData(4)]
    [InlineData(0)]
    [InlineData(42)]
    [InlineData(-1)]
    public void OperStatus_AnythingElse_IsUnknown(int raw)
    {
        Map(operStatus: raw).OperStatus.Should().Be(InterfaceStatus.Unknown);
    }

    /// <summary>
    /// <c>5</c> is <c>dormant</c> on <c>ifOperStatus</c> and undefined on <c>ifAdminStatus</c>.
    /// Reading it as dormant on both would report an administrative intent no device expressed.
    /// </summary>
    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(7)]
    public void AdminStatus_AValueOnlyTheOperCounterDefines_IsUnknown(int raw)
    {
        Map(adminStatus: raw).AdminStatus.Should().Be(InterfaceStatus.Unknown);
    }

    /// <summary>
    /// An interface that answered neither counter is <see cref="InterfaceStatus.Unknown"/> on
    /// both, rather than defaulting to the first member of an enum.
    /// </summary>
    [Fact]
    public void AnInterfaceThatAnsweredNeitherCounter_IsUnknownOnBoth()
    {
        DeviceInterfaceSummary summary = Map(adminStatus: null, operStatus: null);

        summary.AdminStatus.Should().Be(InterfaceStatus.Unknown);
        summary.OperStatus.Should().Be(InterfaceStatus.Unknown);
    }

    [Fact]
    public void EverythingElseOnTheRow_TravelsUnchanged()
    {
        DateTimeOffset firstSeen = new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);
        DateTimeOffset lastSeen = new(2026, 9, 8, 10, 0, 0, TimeSpan.Zero);

        DeviceInterface row = new()
        {
            Id = Guid.CreateVersion7(),
            DeviceId = Guid.CreateVersion7(),
            IfIndex = 7,
            Name = "Gi0/7",
            Description = "GigabitEthernet0/7",
            Alias = "uplink to core",
            InterfaceType = 6,
            Mtu = 9216,
            SpeedBitsPerSecond = 10_000_000_000,
            PhysicalAddress = "00:1A:2B:3C:4D:07",
            AdminStatus = 1,
            OperStatus = 1,
            FirstSeenAt = firstSeen,
            LastSeenAt = lastSeen
        };

        DeviceInterfaceSummary summary = row.ToSummary();

        summary.Id.Should().Be(row.Id);
        summary.IfIndex.Should().Be(7);
        summary.Name.Should().Be("Gi0/7");
        summary.Description.Should().Be("GigabitEthernet0/7");
        summary.Alias.Should().Be("uplink to core");
        summary.InterfaceType.Should().Be(6);
        summary.Mtu.Should().Be(9216);
        summary.SpeedBitsPerSecond.Should().Be(10_000_000_000);
        summary.PhysicalAddress.Should().Be("00:1A:2B:3C:4D:07");
        summary.FirstSeenAt.Should().Be(firstSeen);
        summary.LastSeenAt.Should().Be(lastSeen);
    }

    private static DeviceInterfaceSummary Map(int? adminStatus = 1, int? operStatus = 1) =>
        new DeviceInterface
        {
            Id = Guid.CreateVersion7(),
            DeviceId = Guid.CreateVersion7(),
            IfIndex = 1,
            AdminStatus = adminStatus,
            OperStatus = operStatus
        }.ToSummary();
}
