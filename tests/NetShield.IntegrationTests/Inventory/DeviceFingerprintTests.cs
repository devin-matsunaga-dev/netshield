using FluentAssertions;

using NetShield.Contracts.Inventory;
using NetShield.Contracts.Inventory.Events;

using NetShield.IntegrationTests.Platform;

namespace NetShield.IntegrationTests.Inventory;

/// <summary>
/// The whole fingerprint round trip, through the real queue, the real contract and the real
/// outbox: a walk is asked for, a collector reports what it found, and the device learns what it
/// is — or does not.
/// </summary>
/// <remarks>
/// This is the second subscriber to <c>CollectorJobCompleted</c>, beside reachability's. The two
/// share a table of jobs and read only their own rows, which several of the tests below are
/// about.
/// </remarks>
public sealed class DeviceFingerprintTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ASuccessfulWalk_RecordsTheFingerprintOnTheDevice()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await DiscoveryFixtures.WalkAsync(host, deviceId, DiscoveryFixtures.WalkResult(), Cancellation);

        DeviceFacts device = await host.DeviceFactsAsync(deviceId, Cancellation);

        device.Vendor.Should().Be(DeviceVendor.CiscoIos);
        device.Model.Should().Be("WS-C2960X-48FPD-L");
        device.OsVersion.Should().Be("15.2(7)E3");
        device.SerialNumber.Should().Be("FOC1234X5YZ");

        FingerprintRow? fingerprint = await host.FingerprintAsync(deviceId, Cancellation);

        fingerprint.Should().NotBeNull();
        fingerprint!.SysObjectId.Should().Be("1.3.6.1.4.1.9.1.2494");
        fingerprint.SysName.Should().Be("lab-sw-ios-01");
        fingerprint.UptimeSeconds.Should().Be(1234567.89);
        fingerprint.ReducedCapability.Should().BeFalse();
        fingerprint.LastWalkAt.Should().NotBeNull();
        fingerprint.LastError.Should().BeNull();
        fingerprint.OverriddenFields.Should().BeEmpty();
    }

    [Fact]
    public async Task ASuccessfulWalk_RecordsTheInterfaceInventory()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await DiscoveryFixtures.WalkAsync(host, deviceId, DiscoveryFixtures.WalkResult(), Cancellation);

        IReadOnlyList<InterfaceRow> interfaces = await host.InterfacesAsync(deviceId, Cancellation);

        interfaces.Should().HaveCount(2);

        InterfaceRow first = interfaces[0];

        first.IfIndex.Should().Be(1);
        first.Name.Should().Be("Gi0/1");
        first.Description.Should().Be("GigabitEthernet0/1");
        first.Alias.Should().Be("port 1");
        first.InterfaceType.Should().Be(6);
        first.Mtu.Should().Be(1500);
        first.SpeedBitsPerSecond.Should().Be(1_000_000_000);
        first.PhysicalAddress.Should().Be("00:1A:2B:3C:4D:01");
        first.AdminStatus.Should().Be(1);
        first.OperStatus.Should().Be(1);
    }

    [Fact]
    public async Task AnUnrecognisedDevice_LandsAsGenericSnmpWithReducedCapabilityFlagged()
    {
        // SPEC.md §4, and the WP-1.5 criterion: the fallback is recorded as a fact so the UI can
        // label it, rather than being something a screen infers from the vendor name.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await DiscoveryFixtures.WalkAsync(
            host,
            deviceId,
            DiscoveryFixtures.WalkResult(
                vendor: "GenericSnmp",
                reducedCapability: true,
                sysObjectId: "1.3.6.1.4.1.99999.1.2.3",
                sysDescr: "Example Networks SmartSwitch, firmware 2.1.4",
                model: "ESW-2400",
                osVersion: "2.1.4",
                serialNumber: "EXN0001234"),
            Cancellation);

        (await host.DeviceFactsAsync(deviceId, Cancellation)).Vendor.Should().Be(DeviceVendor.GenericSnmp);

        FingerprintRow? fingerprint = await host.FingerprintAsync(deviceId, Cancellation);

        fingerprint!.ReducedCapability.Should().BeTrue();
        fingerprint.Model.Should().Be("ESW-2400");
    }

    [Fact]
    public async Task AWalkTheCollectorCouldNotPerform_LeavesTheFingerprintUntouched()
    {
        // The distinction the package rests on, and the same one WP-1.4 drew for device state:
        // an unreachable device is not a device that has become unidentifiable.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await DiscoveryFixtures.WalkAsync(host, deviceId, DiscoveryFixtures.WalkResult(), Cancellation);

        await DiscoveryFixtures.FailWalkAsync(
            host, deviceId, "The device did not answer a read of 6 objects: No SNMP response received.", Cancellation);

        DeviceFacts device = await host.DeviceFactsAsync(deviceId, Cancellation);

        device.Vendor.Should().Be(DeviceVendor.CiscoIos);
        device.SerialNumber.Should().Be("FOC1234X5YZ");

        FingerprintRow? fingerprint = await host.FingerprintAsync(deviceId, Cancellation);

        fingerprint!.LastError.Should().Contain("No SNMP response");
        fingerprint.Model.Should().Be("WS-C2960X-48FPD-L", "a failed walk establishes nothing");
        (await host.InterfacesAsync(deviceId, Cancellation)).Should().HaveCount(2);
    }

    [Fact]
    public async Task AFirstWalkThatFails_RecordsTheReasonAndIdentifiesNothing()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await DiscoveryFixtures.FailWalkAsync(host, deviceId, "Timed out after 120 seconds.", Cancellation);

        FingerprintRow? fingerprint = await host.FingerprintAsync(deviceId, Cancellation);

        fingerprint!.LastError.Should().Contain("Timed out");
        fingerprint.LastWalkAt.Should().BeNull("no walk has ever reached this device");
        fingerprint.Vendor.Should().Be(DeviceVendor.Unknown);
    }

    [Fact]
    public async Task ASuccessfulJobCarryingSomethingElse_IsRecordedAsUnreadable()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await DiscoveryFixtures.WalkAsync(host, deviceId, """{ "somethingElse": true }""", Cancellation);

        FingerprintRow? fingerprint = await host.FingerprintAsync(deviceId, Cancellation);

        fingerprint!.LastError.Should().Contain("could not read");
        (await host.DeviceFactsAsync(deviceId, Cancellation)).Vendor.Should().Be(DeviceVendor.CiscoIos,
            "the device was created as CiscoIos and nothing readable has said otherwise");
        (await host.InterfacesAsync(deviceId, Cancellation)).Should().BeEmpty();
    }

    [Fact]
    public async Task AResultDeliveredTwice_IsAppliedOnce()
    {
        // Outbox delivery is at-least-once. A second application would re-run the override
        // comparison against a baseline the first application had already moved.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await DiscoveryFixtures.WalkAsync(host, deviceId, DiscoveryFixtures.WalkResult(), Cancellation);

        await host.RedeliverOutboxAsync(Cancellation);

        (await host.InterfacesAsync(deviceId, Cancellation)).Should().HaveCount(2);
        (await host.OutboxPayloadsAsync<DeviceFingerprinted>(Cancellation)).Should().ContainSingle();
    }

    // --- Reconciling the interface inventory ---------------------------------------------------

    [Fact]
    public async Task AnInterfaceThatIsGoneFromACompleteWalk_IsRemoved()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await DiscoveryFixtures.WalkAsync(
            host, deviceId, DiscoveryFixtures.WalkResult(interfaces: [1, 2, 3]), Cancellation);

        (await host.InterfacesAsync(deviceId, Cancellation)).Should().HaveCount(3);

        await DiscoveryFixtures.WalkAsync(
            host, deviceId, DiscoveryFixtures.WalkResult(interfaces: [1, 3]), Cancellation);

        (await host.InterfacesAsync(deviceId, Cancellation))
            .Select(row => row.IfIndex).Should().Equal(1, 3);
    }

    [Fact]
    public async Task AnInterfaceMissingFromATruncatedWalk_IsKept()
    {
        // A walk that hit the ceiling read part of the table, so an interface it did not mention
        // may simply not have been reached. Deleting on that evidence would empty the inventory
        // of the largest devices in the estate.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await DiscoveryFixtures.WalkAsync(
            host, deviceId, DiscoveryFixtures.WalkResult(interfaces: [1, 2, 3]), Cancellation);

        await DiscoveryFixtures.WalkAsync(
            host,
            deviceId,
            DiscoveryFixtures.WalkResult(interfaces: [1], truncated: true, interfaceCount: 3),
            Cancellation);

        (await host.InterfacesAsync(deviceId, Cancellation)).Should().HaveCount(3);
        (await host.FingerprintAsync(deviceId, Cancellation))!.InterfacesTruncated.Should().BeTrue();
    }

    [Fact]
    public async Task AnInterfaceThatSurvivesAWalk_KeepsWhenItWasFirstSeen()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await DiscoveryFixtures.WalkAsync(host, deviceId, DiscoveryFixtures.WalkResult(), Cancellation);

        DateTimeOffset firstSeen = (await host.InterfacesAsync(deviceId, Cancellation))[0].FirstSeenAt;

        await DiscoveryFixtures.WalkAsync(
            host, deviceId, DiscoveryFixtures.WalkResult(operStatus: 2), Cancellation);

        InterfaceRow row = (await host.InterfacesAsync(deviceId, Cancellation))[0];

        row.FirstSeenAt.Should().Be(firstSeen, "a port that is still there did not just appear");
        row.LastSeenAt.Should().BeOnOrAfter(firstSeen);
        row.OperStatus.Should().Be(2);
    }

    // --- Discovered against overridden ---------------------------------------------------------

    [Fact]
    public async Task AFirstWalk_CorrectsWhatAnOperatorGuessedWhenTheyAddedTheDevice()
    {
        // The device is created as CiscoIos by the fixture. The walk says NX-OS, and wins:
        // nothing has ever been discovered, so there is nothing to have been overridden.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await DiscoveryFixtures.WalkAsync(
            host, deviceId, DiscoveryFixtures.WalkResult(vendor: "CiscoNxOs"), Cancellation);

        (await host.DeviceFactsAsync(deviceId, Cancellation)).Vendor.Should().Be(DeviceVendor.CiscoNxOs);
        (await host.FingerprintAsync(deviceId, Cancellation))!.OverriddenFields.Should().BeEmpty();
    }

    [Fact]
    public async Task AValueAnOperatorSetAfterAWalk_IsNotOverwrittenByTheNextOne()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await DiscoveryFixtures.WalkAsync(host, deviceId, DiscoveryFixtures.WalkResult(), Cancellation);

        await SetModelAsync(host, deviceId, "WS-C2960X-48FPD-L (spare chassis)");

        await DiscoveryFixtures.WalkAsync(host, deviceId, DiscoveryFixtures.WalkResult(), Cancellation);

        DeviceFacts device = await host.DeviceFactsAsync(deviceId, Cancellation);

        device.Model.Should().Be("WS-C2960X-48FPD-L (spare chassis)");

        FingerprintRow? fingerprint = await host.FingerprintAsync(deviceId, Cancellation);

        fingerprint!.OverriddenFields.Should().Equal("model");
        fingerprint.Model.Should().Be("WS-C2960X-48FPD-L", "what the walk saw is still recorded");
    }

    [Fact]
    public async Task AFieldNobodyPinned_IsStillCorrectedByALaterWalk()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await DiscoveryFixtures.WalkAsync(host, deviceId, DiscoveryFixtures.WalkResult(), Cancellation);

        await SetModelAsync(host, deviceId, "WS-C2960X-48FPD-L (spare chassis)");

        await DiscoveryFixtures.WalkAsync(
            host, deviceId, DiscoveryFixtures.WalkResult(osVersion: "15.2(7)E9"), Cancellation);

        DeviceFacts device = await host.DeviceFactsAsync(deviceId, Cancellation);

        device.Model.Should().Be("WS-C2960X-48FPD-L (spare chassis)", "the operator pinned it");
        device.OsVersion.Should().Be("15.2(7)E9", "and nobody pinned this one");
    }

    [Fact]
    public async Task AFactTheWalkDidNotEstablish_DoesNotEraseWhatIsKnown()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await DiscoveryFixtures.WalkAsync(host, deviceId, DiscoveryFixtures.WalkResult(), Cancellation);

        await DiscoveryFixtures.WalkAsync(
            host, deviceId, DiscoveryFixtures.WalkResult(serialNumber: null), Cancellation);

        (await host.DeviceFactsAsync(deviceId, Cancellation)).SerialNumber.Should().Be("FOC1234X5YZ");
        (await host.FingerprintAsync(deviceId, Cancellation))!.OverriddenFields.Should().BeEmpty(
            "an absent answer is not an operator override");
    }

    [Fact]
    public async Task AWalkNamingAVendorThisBuildDoesNotHave_LeavesTheVendorAloneAndSaysSo()
    {
        // The two vendor lists are matched by string across two repositories. A collector newer
        // than the API must not be able to make a recognised device look unrecognised.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await DiscoveryFixtures.WalkAsync(
            host, deviceId, DiscoveryFixtures.WalkResult(vendor: "SomeFutureVendor"), Cancellation);

        (await host.DeviceFactsAsync(deviceId, Cancellation)).Vendor.Should().Be(DeviceVendor.CiscoIos);

        FingerprintRow? fingerprint = await host.FingerprintAsync(deviceId, Cancellation);

        fingerprint!.LastError.Should().Contain("SomeFutureVendor");
        fingerprint.Model.Should().Be("WS-C2960X-48FPD-L", "everything else it found still applies");
    }

    // --- What the device row and the outbox say ------------------------------------------------

    [Fact]
    public async Task AWalkThatChangedSomething_StampsTheDeviceAndPublishesOnce()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        DateTimeOffset before = (await host.DeviceFactsAsync(deviceId, Cancellation)).UpdatedAt;

        await DiscoveryFixtures.WalkAsync(host, deviceId, DiscoveryFixtures.WalkResult(), Cancellation);

        (await host.DeviceFactsAsync(deviceId, Cancellation)).UpdatedAt.Should().BeAfter(before,
            "what the device is has changed, which is what the device list's updated_at is for");

        IReadOnlyList<DeviceFingerprinted> events =
            await host.OutboxPayloadsAsync<DeviceFingerprinted>(Cancellation);

        events.Should().ContainSingle();
        events[0].DeviceId.Should().Be(deviceId);
        events[0].Hostname.Should().Be("switch-01");
        events[0].Vendor.Should().Be(DeviceVendor.CiscoIos);
        events[0].SerialNumber.Should().Be("FOC1234X5YZ");
        events[0].InterfaceCount.Should().Be(2);
        events[0].ReducedCapability.Should().BeFalse();
    }

    [Fact]
    public async Task ReWalkingAnUnchangedDevice_PublishesNothingAndStampsNothing()
    {
        // Otherwise every subscriber rebuilds whatever it caches each time somebody re-walks a
        // switch — the rule DeviceCredentialProfilesChanged already follows.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await DiscoveryFixtures.WalkAsync(host, deviceId, DiscoveryFixtures.WalkResult(), Cancellation);

        DateTimeOffset stamped = (await host.DeviceFactsAsync(deviceId, Cancellation)).UpdatedAt;

        await DiscoveryFixtures.WalkAsync(host, deviceId, DiscoveryFixtures.WalkResult(), Cancellation);

        (await host.DeviceFactsAsync(deviceId, Cancellation)).UpdatedAt.Should().Be(stamped);
        (await host.OutboxPayloadsAsync<DeviceFingerprinted>(Cancellation)).Should().ContainSingle();
    }

    [Fact]
    public async Task AnInterfaceAppearing_IsEnoughToPublishEvenWhenTheIdentityDidNot()
    {
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await DiscoveryFixtures.WalkAsync(host, deviceId, DiscoveryFixtures.WalkResult(), Cancellation);

        await DiscoveryFixtures.WalkAsync(
            host, deviceId, DiscoveryFixtures.WalkResult(interfaces: [1, 2, 3]), Cancellation);

        (await host.OutboxPayloadsAsync<DeviceFingerprinted>(Cancellation)).Should().HaveCount(2);
    }

    [Fact]
    public async Task AnInterfaceMerelyChangingStatus_IsNotAFingerprintChange()
    {
        // Operational status is telemetry, and Phase 3 owns it. A port flapping must not put an
        // event on the outbox every time somebody walks the switch.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await DiscoveryFixtures.WalkAsync(host, deviceId, DiscoveryFixtures.WalkResult(), Cancellation);

        await DiscoveryFixtures.WalkAsync(
            host, deviceId, DiscoveryFixtures.WalkResult(operStatus: 2), Cancellation);

        (await host.OutboxPayloadsAsync<DeviceFingerprinted>(Cancellation)).Should().ContainSingle();
        (await host.InterfacesAsync(deviceId, Cancellation))[0].OperStatus.Should().Be(2);
    }

    [Fact]
    public async Task AWalkDoesNotDisturbWhatReachabilityRecorded()
    {
        // The two subscribers share one table of jobs and read only their own rows.
        await using InventoryHost host = await InventoryHost.StartAsync(postgres, Cancellation);

        Guid deviceId = await DiscoveryFixtures.CreateWalkableDeviceAsync(host, Cancellation);

        await host.ScheduleReachabilityAsync(Cancellation);
        await ReachabilityFixtures.CompleteOneAsync(host, sent: 4, received: 4, Cancellation);

        ReachabilityRow? before = await host.ReachabilityAsync(deviceId, Cancellation);

        await DiscoveryFixtures.WalkAsync(host, deviceId, DiscoveryFixtures.WalkResult(), Cancellation);

        ReachabilityRow? after = await host.ReachabilityAsync(deviceId, Cancellation);

        after!.PendingObservations.Should().Be(before!.PendingObservations);
        after.LastAppliedJobId.Should().Be(before.LastAppliedJobId);
        after.LastError.Should().BeNull();
    }

    private static async Task SetModelAsync(InventoryHost host, Guid deviceId, string model)
    {
        // A whole-resource PUT, which is what WP-1.1 settled an update is. Everything else is
        // sent back as it stands so that only the model moves.
        DeviceFacts facts = await host.DeviceFactsAsync(deviceId, Cancellation);

        await host.Client.PutAsync(
            $"/api/v1/devices/{deviceId}",
            new UpdateDeviceRequest(
                "switch-01",
                "10.10.0.1",
                facts.Vendor,
                model,
                facts.OsVersion,
                facts.SerialNumber,
                Role: DeviceRole.Switch),
            Cancellation);
    }
}
