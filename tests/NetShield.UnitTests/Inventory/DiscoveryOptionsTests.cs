using System.ComponentModel.DataAnnotations;

using FluentAssertions;

using NetShield.Contracts.Inventory;

using NetShield.Inventory.Discovery;

namespace NetShield.UnitTests.Inventory;

/// <summary>
/// The discovery settings' defaults and their bounds. They are validated on start, so a
/// deployment that sets one of them wrongly fails to come up rather than walking devices oddly.
/// </summary>
public sealed class DiscoveryOptionsTests
{
    [Fact]
    public void Defaults_AreTheOnesTheWalkIsDesignedAround()
    {
        DiscoveryOptions options = new();

        options.RequestTimeoutSeconds.Should().Be(5);
        options.Retries.Should().Be(2);
        options.MaxRepetitions.Should().Be(25);
        options.MaxRowsPerSubtree.Should().Be(20_000);
        options.MaxInterfaces.Should().Be(512);
    }

    [Fact]
    public void SweepDefaults_AreTheOnesTheScheduleIsDesignedAround()
    {
        DiscoveryOptions options = new();

        options.ScheduleEnabled.Should().BeTrue();
        options.ScanIntervalSeconds.Should().Be(60);
        options.MaxRunsPerScan.Should().Be(2);
        options.MaxAddressesPerJob.Should().Be(256);
        options.MaxAddressesPerRun.Should().Be(65_536);
        options.MaxJobsPerRun.Should().Be(512);
        options.SweepProbeCount.Should().Be(1);
        options.SweepTimeoutSeconds.Should().Be(1);
        options.SweepIntervalSeconds.Should().Be(0);
        options.SweepConcurrency.Should().Be(64);
        options.MaxRespondersPerJob.Should().Be(1024);
    }

    [Fact]
    public void CredentialKindOrder_ConfiguredByNobody_IsTheRuleWpOneFiveWroteIntoTheHandler()
    {
        // The WP-1.5 order, now configurable rather than compiled in. It has to stay the default,
        // or an installation that never set it would silently change which credential a walk uses.
        new DiscoveryOptions().ResolvedCredentialKindOrder
            .Should().Equal(CredentialKind.SnmpV3, CredentialKind.SnmpV2c);
    }

    [Fact]
    public void CredentialKindOrder_Configured_ReplacesTheDefaultRatherThanExtendingIt()
    {
        // The property starts empty on purpose: the configuration binder adds to a collection
        // that already holds something, so a property carrying its own default could only ever
        // be appended to — and the default's first entry would keep winning.
        DiscoveryOptions options = new();

        options.CredentialKindOrder.Add(CredentialKind.SnmpV2c);

        options.ResolvedCredentialKindOrder.Should().Equal(CredentialKind.SnmpV2c);
    }

    [Fact]
    public void MaxRespondersPerJob_StaysWithinTheResultPayloadCeiling()
    {
        // The same arithmetic MaxInterfaces has to satisfy: CollectorLimits.ResultLength is
        // 256 KiB, and a responder is an address and a round trip.
        const int BytesPerResponder = 64;

        (new DiscoveryOptions().MaxRespondersPerJob * BytesPerResponder).Should().BeLessThan(256 * 1024);
    }

    [Fact]
    public void MaxAddressesPerJob_IsNotLargerThanTheCollectorWillSweep()
    {
        // collector/discovery/sweep.py refuses a span above 65,536 addresses outright. A default
        // above that would queue jobs the collector fails on sight.
        new DiscoveryOptions().MaxAddressesPerJob.Should().BeLessThanOrEqualTo(65_536);
    }

    [Fact]
    public void Defaults_AreValid()
    {
        Validate(new DiscoveryOptions()).Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(200)]
    public void RequestTimeout_OutsideItsRange_IsRefused(double seconds)
    {
        Validate(new DiscoveryOptions { RequestTimeoutSeconds = seconds })
            .Should().ContainSingle();
    }

    [Fact]
    public void MaxInterfaces_StaysWithinTheResultPayloadCeiling()
    {
        // CollectorLimits.ResultLength is 256 KiB and an interface is a couple of hundred bytes
        // of JSON. The default has to leave that room, or a walk of a large device is refused at
        // submission and wasted.
        const int BytesPerInterface = 256;

        (new DiscoveryOptions().MaxInterfaces * BytesPerInterface).Should().BeLessThan(256 * 1024);
    }

    private static IReadOnlyList<ValidationResult> Validate(DiscoveryOptions options)
    {
        List<ValidationResult> results = [];

        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);

        return results;
    }
}
