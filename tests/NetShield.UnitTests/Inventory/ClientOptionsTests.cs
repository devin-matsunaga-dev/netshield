using System.ComponentModel.DataAnnotations;

using FluentAssertions;

using NetShield.Inventory.Clients;
using NetShield.Inventory.Collector;

namespace NetShield.UnitTests.Inventory;

/// <summary>
/// The client-tracking settings, and the relationships between them that make the schedule sane.
/// </summary>
/// <remarks>
/// Validated on start, like every other option set in the system: a host that came up and only
/// discovered a nonsensical interval on the first scan would pass its health checks and look
/// fine while quietly reading nothing.
/// </remarks>
public sealed class ClientOptionsTests
{
    [Fact]
    public void TheDefaults_AreValid() => Validate(new ClientOptions()).Should().BeEmpty();

    [Fact]
    public void TheDefaultWalkInterval_IsMuchLongerThanAReachabilityProbe() =>
        // A forwarding database is a fact about where things are plugged in, and a switch does
        // not re-cable itself every minute. Reading it at the reachability cadence would be five
        // hundred SNMP sessions a minute for an answer that changes hourly.
        new ClientOptions().WalkIntervalSeconds.Should().BeGreaterThan(600);

    [Fact]
    public void TheDefaultScanInterval_IsShorterThanTheDefaultWalkInterval() =>
        // The scan is the resolution at which a due device is noticed, not a rate of its own.
        new ClientOptions().ScanIntervalSeconds.Should()
            .BeLessThan(new ClientOptions().WalkIntervalSeconds);

    [Fact]
    public void TheDefaultCacheLifetime_IsShorterThanTheWalkInterval()
    {
        // The entry lifetime is the bound on how stale an answer can be when an invalidation was
        // itself lost — a Redis unreachable for the one second the invalidation ran. Longer than
        // the walk interval would mean a whole walk's worth of handovers could go unseen.
        ClientOptions options = new();

        options.ResolutionCacheSeconds.Should().BeLessThan(options.WalkIntervalSeconds);
    }

    [Fact]
    public void TheDefaultRequestTimeout_FitsManyTimesInsideTheDefaultLease()
    {
        // A client walk is several subtree walks of tables far larger than an interface list, so
        // it is the longest job NetShield queues. It still has to finish inside the lease, or a
        // second collector is handed it while the first is still reading.
        ClientOptions clients = new();
        CollectorJobOptions jobs = new();

        clients.RequestTimeoutSeconds.Should().BeLessThan(jobs.LeaseSeconds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(59)]
    [InlineData(86_401)]
    public void AWalkIntervalOutsideTheRange_IsRefused(int seconds) =>
        Validate(new ClientOptions { WalkIntervalSeconds = seconds })
            .Should().Contain(result => result.MemberNames.Contains(nameof(ClientOptions.WalkIntervalSeconds)));

    [Theory]
    [InlineData(0)]
    [InlineData(50_001)]
    public void ANeighbourCeilingOutsideTheRange_IsRefused(int entries) =>
        Validate(new ClientOptions { MaxNeighbors = entries })
            .Should().Contain(result => result.MemberNames.Contains(nameof(ClientOptions.MaxNeighbors)));

    [Fact]
    public void TheReportCeilings_AreNotAboveTheSubtreeCeiling()
    {
        // Reporting more rows than the walk is allowed to read is a ceiling that can never bind,
        // which reads as a bound and is not one.
        ClientOptions options = new();

        options.MaxNeighbors.Should().BeLessThanOrEqualTo(options.MaxRowsPerSubtree);
        options.MaxForwardingEntries.Should().BeLessThanOrEqualTo(options.MaxRowsPerSubtree);
    }

    [Fact]
    public void TheSectionName_IsTheOneTheModuleBindsFrom() =>
        ClientOptions.SectionName.Should().Be("Inventory:Clients");

    private static IReadOnlyList<ValidationResult> Validate(ClientOptions options)
    {
        List<ValidationResult> results = [];

        Validator.TryValidateObject(options, new ValidationContext(options), results, true);

        return results;
    }
}
