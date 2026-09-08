using System.Net;

using FluentAssertions;

using NetShield.Contracts.Inventory;

using NetShield.Inventory.Resolution;

namespace NetShield.UnitTests.Inventory;

/// <summary>
/// The order between the two things that can claim an address, as arithmetic.
/// </summary>
/// <remarks>
/// <c>ROADMAP.md</c> calls WP-1.8 the single highest-leverage package in the build and the
/// easiest to get subtly wrong, and this is the file it means. Every Phase 4 flow record and
/// every Phase 5 log event is attributed through this decision; an answer that is wrong at a
/// handover boundary is wrong silently, in data nobody re-reads. So the decision is tested with
/// no database, no cache and no collector in the way — just intervals and instants.
/// </remarks>
public sealed class AssetResolutionRuleTests
{
    private static readonly IPAddress Address = IPAddress.Parse("10.0.0.5");
    private static readonly DateTimeOffset Noon = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid FirstClient = Guid.CreateVersion7(Noon.AddHours(-4));
    private static readonly Guid SecondClient = Guid.CreateVersion7(Noon.AddHours(-1));
    private static readonly Guid DeviceId = Guid.CreateVersion7(Noon.AddDays(-30));

    // --- The handover. WP-1.8's own "Done when": an IP reassigned between two hosts resolves to
    // the correct host for a timestamp on either side of the handover.

    [Fact]
    public void Apply_BeforeAHandover_ResolvesToThePreviousHolder()
    {
        AssetResolution resolution = AssetResolutionRule.Apply(Address, Noon.AddMinutes(-1), Handover());

        resolution.Kind.Should().Be(AssetKind.Client);
        resolution.ClientId.Should().Be(FirstClient);
        resolution.MacAddress.Should().Be("AA:AA:AA:00:00:01");
    }

    [Fact]
    public void Apply_AfterAHandover_ResolvesToTheNewHolder()
    {
        AssetResolution resolution = AssetResolutionRule.Apply(Address, Noon.AddMinutes(1), Handover());

        resolution.Kind.Should().Be(AssetKind.Client);
        resolution.ClientId.Should().Be(SecondClient);
        resolution.MacAddress.Should().Be("BB:BB:BB:00:00:02");
    }

    [Fact]
    public void Apply_AtTheInstantOfAHandover_ResolvesToTheNewHolder()
    {
        // The interval is half-open: ObservedFrom is inside it and ObservedTo is not. That is
        // what makes the boundary instant belong to exactly one of the two intervals meeting at
        // it — without it the boundary would be in both, or in neither.
        AssetResolution resolution = AssetResolutionRule.Apply(Address, Noon, Handover());

        resolution.Kind.Should().Be(AssetKind.Client);
        resolution.ClientId.Should().Be(SecondClient);
    }

    [Fact]
    public void Apply_CarriesTheIntervalTheAnswerCameFrom()
    {
        // A resolution nobody can check is one nobody should trust: an operator looking at a flow
        // attributed to the wrong host needs to see which observation attributed it.
        AssetResolution resolution = AssetResolutionRule.Apply(Address, Noon.AddMinutes(-1), Handover());

        resolution.ObservedFrom.Should().Be(Noon.AddHours(-3));
        resolution.ObservedTo.Should().Be(Noon);
        resolution.MacAddress.Should().Be("AA:AA:AA:00:00:01");
    }

    [Fact]
    public void Apply_BeforeAnythingWasEverObserved_IsUnresolved()
    {
        AssetResolution resolution = AssetResolutionRule.Apply(Address, Noon.AddDays(-10), Handover());

        resolution.Kind.Should().Be(AssetKind.Unresolved);
        resolution.ClientId.Should().BeNull();
        resolution.DeviceId.Should().BeNull();
    }

    [Fact]
    public void Apply_WithNoHistoryAtAll_IsUnresolved()
    {
        // An address outside the estate is the ordinary case, and ARCHITECTURE.md §6 wants an
        // event landed with a null asset reference rather than a write that failed.
        AssetResolution resolution = AssetResolutionRule.Apply(Address, Noon, Empty());

        resolution.Kind.Should().Be(AssetKind.Unresolved);
        resolution.IpAddress.Should().Be("10.0.0.5");
        resolution.At.Should().Be(Noon);
    }

    // --- Device against client.

    [Fact]
    public void Apply_WhenADeviceHoldsTheAddress_PrefersTheDeviceOverTheArpObservation()
    {
        // A router's own interface: a neighbouring router's ARP cache reports it, so a client row
        // exists for its MAC. The asset an operator means is still the device.
        AssetResolution resolution = AssetResolutionRule.Apply(Address, Noon.AddMinutes(1), DeviceAndClient());

        resolution.Kind.Should().Be(AssetKind.Device);
        resolution.DeviceId.Should().Be(DeviceId);
        resolution.DeviceHostname.Should().Be("core-rtr-01");
    }

    [Fact]
    public void Apply_WhenADeviceHoldsTheAddress_StillCarriesTheObservedMac()
    {
        // Evidence, not noise. It is what lets somebody see that the device's address is also
        // being answered by a particular piece of hardware.
        AssetResolution resolution = AssetResolutionRule.Apply(Address, Noon.AddMinutes(1), DeviceAndClient());

        resolution.MacAddress.Should().Be("BB:BB:BB:00:00:02");
        resolution.ClientId.Should().Be(SecondClient);
    }

    [Fact]
    public void Apply_AtAnInstantBeforeTheDeviceExisted_PrefersTheClient()
    {
        // The promotion case, and the reason the device's creation time is read at all. A
        // discovery candidate promoted to a device last Tuesday cannot have been the asset on
        // Monday, and the client observation is the only evidence about Monday there is.
        AddressHistory history = new(
            new CachedDevice(DeviceId, "core-rtr-01", Noon.AddMinutes(30)),
            Handover().Bindings,
            Horizon: null);

        AssetResolution resolution = AssetResolutionRule.Apply(Address, Noon.AddMinutes(1), history);

        resolution.Kind.Should().Be(AssetKind.Client);
        resolution.ClientId.Should().Be(SecondClient);
        resolution.DeviceId.Should().BeNull();
    }

    [Fact]
    public void Apply_WithADeviceAndNoObservationAtThatInstant_ResolvesToTheDeviceAnyway()
    {
        // Case 3, and the one place the WP-1.1 limitation shows: devices keep one address and no
        // record of what they held before, so a device is resolved from the address it is
        // reached on now. The creation time is a tie-break between two candidates and never a
        // gate on the only one — otherwise an instant a minute before the row was created would
        // answer "unresolved" for an address a device plainly holds.
        AddressHistory history = new(
            new CachedDevice(DeviceId, "core-rtr-01", Noon),
            [],
            Horizon: null);

        AssetResolution resolution = AssetResolutionRule.Apply(Address, Noon.AddYears(-1), history);

        resolution.Kind.Should().Be(AssetKind.Device);
        resolution.DeviceId.Should().Be(DeviceId);
        resolution.MacAddress.Should().BeNull();
    }

    // --- The cached window.

    [Fact]
    public void Covers_AnInstantInsideTheCachedWindow_IsAnswerable()
    {
        AddressHistory history = new(null, [], Horizon: Noon);

        history.Covers(Noon).Should().BeTrue();
        history.Covers(Noon.AddSeconds(1)).Should().BeTrue();
    }

    [Fact]
    public void Covers_AnInstantOlderThanTheCachedWindow_IsNot()
    {
        // Not a wrong answer — the resolver reads through to the database for these. Without the
        // distinction, an address in a busy DHCP pool would start answering "unresolved" for
        // anything older than the window.
        AddressHistory history = new(null, [], Horizon: Noon);

        history.Covers(Noon.AddTicks(-1)).Should().BeFalse();
    }

    [Fact]
    public void Covers_AHistoryWithNoHorizon_AnswersForEveryInstant()
    {
        AddressHistory history = new(null, [], Horizon: null);

        history.Covers(DateTimeOffset.MinValue).Should().BeTrue();
    }

    /// <summary>
    /// One address, held by one client until noon and by another after it — with the intervals
    /// abutting exactly, which is what "no gaps" means.
    /// </summary>
    private static AddressHistory Handover() =>
        new(
            Device: null,
            [
                new CachedBinding(
                    SecondClient,
                    "BB:BB:BB:00:00:02",
                    ClientObservationSource.ArpTable,
                    Noon,
                    ObservedTo: null),
                new CachedBinding(
                    FirstClient,
                    "AA:AA:AA:00:00:01",
                    ClientObservationSource.ArpTable,
                    Noon.AddHours(-3),
                    Noon)
            ],
            Horizon: null);

    private static AddressHistory DeviceAndClient() =>
        new(
            new CachedDevice(DeviceId, "core-rtr-01", Noon.AddDays(-30)),
            Handover().Bindings,
            Horizon: null);

    private static AddressHistory Empty() => new(Device: null, [], Horizon: null);
}
