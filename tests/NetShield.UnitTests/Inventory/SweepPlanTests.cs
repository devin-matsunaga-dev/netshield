using System.Net;

using FluentAssertions;

using NetShield.Inventory.Discovery;

using NetShield.Platform.Results;

namespace NetShield.UnitTests.Inventory;

/// <summary>
/// What a seed actually asks for: how many addresses one run would probe, and which spans the
/// jobs get. One reading, so that the number a validator reports, the number a run records and
/// the work the collector is handed cannot disagree.
/// </summary>
public sealed class SweepPlanTests
{
    [Fact]
    public void AddressCount_OfOneBlock_IsItsProbeableAddresses()
    {
        Plan(["10.0.0.0/24"]).AddressCount.Should().Be(254);
    }

    [Fact]
    public void AddressCount_OfSeveralDisjointBlocks_IsTheirSum()
    {
        Plan(["10.0.0.0/24", "10.0.1.0/24"]).AddressCount.Should().Be(508);
    }

    [Fact]
    public void AddressCount_SubtractsAnExclusionInsideARange()
    {
        Plan(["10.0.0.0/24"], ["10.0.0.128/25"]).AddressCount.Should().Be(127);
    }

    [Fact]
    public void AddressCount_SubtractsOverlappingExclusionsOnlyOnce()
    {
        // Merged before anything is subtracted, or the shared addresses would be counted out
        // twice and the run would under-report what it swept.
        Plan(["10.0.0.0/24"], ["10.0.0.0/25", "10.0.0.0/26"]).AddressCount.Should().Be(127);
    }

    [Fact]
    public void AddressCount_IgnoresAnExclusionOutsideEveryRange()
    {
        Plan(["10.0.0.0/24"], ["198.51.100.0/24"]).AddressCount.Should().Be(254);
    }

    [Fact]
    public void AddressCount_OfAFullyExcludedSeed_IsZero()
    {
        Plan(["10.0.0.0/24"], ["10.0.0.0/24"]).AddressCount.Should().Be(0);
    }

    [Fact]
    public void AddressCount_IgnoresAnExclusionOfAnotherFamily()
    {
        Plan(["10.0.0.0/30"], ["2001:db8::/64"]).AddressCount.Should().Be(2);
    }

    [Fact]
    public void Create_SomethingThatIsNotABlock_Fails()
    {
        Result<SweepPlan> planned = SweepPlan.Create(["10.0.0.0/24", "not-a-block"], null);

        planned.IsSuccess.Should().BeFalse();
        planned.Error!.Code.Should().Be(DiscoveryErrors.InvalidCidrCode);
    }

    [Fact]
    public void Create_AnExclusionThatIsNotABlock_Fails()
    {
        SweepPlan.Create(["10.0.0.0/24"], ["nonsense"]).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void FirstOverlap_FindsTwoRangesThatCoverTheSameAddresses()
    {
        // Refused rather than merged: the address count is a sum, which is only right when the
        // ranges are disjoint, and merging would store something other than what was typed.
        Plan(["10.0.0.0/24", "10.0.0.0/25"]).FirstOverlap().Should().NotBeNull();
    }

    [Fact]
    public void FirstOverlap_OfDisjointRanges_IsNothing()
    {
        Plan(["10.0.0.0/25", "10.0.0.128/25"]).FirstOverlap().Should().BeNull();
    }

    [Fact]
    public void Spans_AreCutToTheCeilingAndCoverEveryProbeableAddress()
    {
        SweepPlan plan = Plan(["10.0.0.0/24"]);

        IReadOnlyList<AddressSpan> spans = [.. plan.Spans(100)];

        spans.Should().HaveCount(3);
        spans.Sum(span => span.Count).Should().Be(254);
    }

    [Fact]
    public void Spans_SkipASpanEveryAddressOfWhichIsExcluded()
    {
        // Queueing a job whose whole answer is "everything here was excluded" spends a lease and
        // a round trip to learn nothing.
        SweepPlan plan = Plan(["10.0.0.0/24"], ["10.0.0.129/25"]);

        IReadOnlyList<AddressSpan> spans = [.. plan.Spans(64)];

        spans.Should().NotContain(span => span.Contains(IPAddress.Parse("10.0.0.200")));
        spans.Sum(plan.Probeable).Should().Be(plan.AddressCount);
    }

    [Fact]
    public void Spans_KeepASpanThatIsOnlyPartlyExcluded()
    {
        // The collector applies the exclusions itself, which is why they travel in the job.
        SweepPlan plan = Plan(["10.0.0.0/24"], ["10.0.0.10/31"]);

        plan.Spans(256).Should().ContainSingle();
    }

    [Fact]
    public void Probeable_OfASpan_ExcludesWhatTheExclusionsCover()
    {
        SweepPlan plan = Plan(["10.0.0.0/24"], ["10.0.0.1/30"]);

        AddressSpan whole = plan.Spans(256).Single();

        // The /30 covers .0 to .3; .0 is not probeable anyway, so three are removed from 254.
        plan.Probeable(whole).Should().Be(251);
    }

    [Fact]
    public void ProbeableSummedOverSpans_IsTheAddressCount()
    {
        SweepPlan plan = Plan(["10.0.0.0/24", "10.0.2.0/25"], ["10.0.0.64/26"]);

        plan.Spans(50).Sum(plan.Probeable).Should().Be(plan.AddressCount);
    }

    private static SweepPlan Plan(IReadOnlyList<string> ranges, IReadOnlyList<string>? exclusions = null)
    {
        Result<SweepPlan> planned = SweepPlan.Create(ranges, exclusions);

        planned.IsSuccess.Should().BeTrue();

        return planned.Value;
    }
}
