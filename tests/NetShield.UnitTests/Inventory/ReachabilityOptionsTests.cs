using System.ComponentModel.DataAnnotations;

using FluentAssertions;

using NetShield.Inventory.Collector;
using NetShield.Inventory.Reachability;

namespace NetShield.UnitTests.Inventory;

/// <summary>
/// The reachability settings, and the relationships between them that make the schedule sane.
/// </summary>
/// <remarks>
/// Validated on start, like every other option set in the system: a host that came up and only
/// discovered a nonsensical interval on the first scan would pass its health checks and look
/// fine while quietly probing nothing.
/// </remarks>
public sealed class ReachabilityOptionsTests
{
    [Fact]
    public void TheDefaults_AreValid() => Validate(new ReachabilityOptions()).Should().BeEmpty();

    [Fact]
    public void TheDefaultPollInterval_IsTheOneSpecTargets() =>
        // SPEC.md §1 designs for 500 devices on a sixty-second interval.
        new ReachabilityOptions().PollIntervalSeconds.Should().Be(60);

    [Fact]
    public void TheDefaultScanInterval_IsShorterThanTheDefaultPollInterval() =>
        // The scan is the resolution at which a due device is noticed, not a rate of its own. A
        // scan slower than the poll interval would silently stretch every device's cadence.
        new ReachabilityOptions().ScanIntervalSeconds.Should()
            .BeLessThan(new ReachabilityOptions().PollIntervalSeconds);

    [Fact]
    public void TheDefaultProbe_FitsInsideTheDefaultLease()
    {
        // A probe that outlived its lease would be handed to a second collector while the first
        // was still running it, and the first one's result would then be refused as stale.
        ReachabilityOptions reachability = new();
        CollectorJobOptions jobs = new();

        double longestProbe = ((reachability.ProbeCount - 1) * reachability.ProbeIntervalSeconds)
            + reachability.ProbeTimeoutSeconds;

        longestProbe.Should().BeLessThan(jobs.LeaseSeconds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public void AProbeCountOutsideTheBounds_IsRefused(int count) =>
        Validate(new ReachabilityOptions { ProbeCount = count }).Should().NotBeEmpty();

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    public void AProbeTimeoutOutsideTheBounds_IsRefused(double seconds) =>
        Validate(new ReachabilityOptions { ProbeTimeoutSeconds = seconds }).Should().NotBeEmpty();

    [Theory]
    [InlineData(-1)]
    [InlineData(11)]
    public void AProbeIntervalOutsideTheBounds_IsRefused(double seconds) =>
        Validate(new ReachabilityOptions { ProbeIntervalSeconds = seconds }).Should().NotBeEmpty();

    [Fact]
    public void AZeroProbeInterval_IsAccepted() =>
        // Zero means send the requests back to back, which is a legitimate choice for a fast
        // link and is why the lower bound is zero rather than a fraction of a second.
        Validate(new ReachabilityOptions { ProbeIntervalSeconds = 0 }).Should().BeEmpty();

    [Theory]
    [InlineData(9)]
    [InlineData(86401)]
    public void APollIntervalOutsideTheBounds_IsRefused(int seconds) =>
        Validate(new ReachabilityOptions { PollIntervalSeconds = seconds }).Should().NotBeEmpty();

    [Theory]
    [InlineData(0)]
    [InlineData(301)]
    public void AScanIntervalOutsideTheBounds_IsRefused(int seconds) =>
        Validate(new ReachabilityOptions { ScanIntervalSeconds = seconds }).Should().NotBeEmpty();

    [Theory]
    [InlineData(0)]
    [InlineData(5001)]
    public void AScanCeilingOutsideTheBounds_IsRefused(int jobs) =>
        Validate(new ReachabilityOptions { MaxJobsPerScan = jobs }).Should().NotBeEmpty();

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public void AFailureThresholdOutsideTheBounds_IsRefused(int probes) =>
        Validate(new ReachabilityOptions { FailureThreshold = probes }).Should().NotBeEmpty();

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public void ASuccessThresholdOutsideTheBounds_IsRefused(int probes) =>
        Validate(new ReachabilityOptions { SuccessThreshold = probes }).Should().NotBeEmpty();

    private static IReadOnlyList<ValidationResult> Validate(object options)
    {
        List<ValidationResult> results = [];

        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);

        return results;
    }
}
