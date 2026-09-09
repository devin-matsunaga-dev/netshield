using FluentAssertions;

using NetShield.Inventory.Topology;
using NetShield.Inventory.Topology.Handlers;

using NetShield.Platform.Paging;
using NetShield.Platform.Results;

namespace NetShield.UnitTests.Inventory;

/// <summary>
/// What the graph endpoint will accept, and what it refuses rather than quietly reinterpreting.
/// </summary>
/// <remarks>
/// The bounds on the traversal are here rather than in the handler because they are the boundary
/// this package was given: a root with an explicit depth, a ceiling on that depth, and nothing
/// that reads as an unbounded walk. A request that crosses one is answered with a 400 naming the
/// field, not with the largest graph the estate can produce.
/// </remarks>
public sealed class TopologyGraphQueryTests
{
    private static PageRequest Page => PageRequest.Create(cursor: null, limit: null).Value!;

    private static Result<TopologyGraphQuery> Create(
        string? site = null,
        int? vlanId = null,
        Guid? rootDeviceId = null,
        int? depth = null) =>
        TopologyGraphQuery.Create(Page, site, vlanId, rootDeviceId, depth);

    [Fact]
    public void WithNoFilters_IsTheWholeEstate()
    {
        TopologyGraphQuery query = Create().Value!;

        query.Site.Should().BeNull();
        query.VlanId.Should().BeNull();
        query.RootDeviceId.Should().BeNull();
        query.Depth.Should().BeNull();
    }

    [Fact]
    public void ASiteIsTrimmed_AndAnEmptyOneIsNoFilter()
    {
        Create(site: "  HQ  ").Value!.Site.Should().Be("HQ");
        Create(site: "   ").Value!.Site.Should().BeNull();
    }

    [Fact]
    public void ARootWithNoDepth_TakesTheDefault()
    {
        Create(rootDeviceId: Guid.NewGuid()).Value!.Depth
            .Should().Be(TopologyLimits.DefaultGraphDepth);
    }

    [Fact]
    public void ADepthWithNoRoot_IsRefused()
    {
        // Answering with the whole estate would be the largest possible answer to a request for
        // a small one.
        Result<TopologyGraphQuery> refused = Create(depth: 2);

        refused.IsSuccess.Should().BeFalse();
        refused.Error!.Code.Should().Be(TopologyErrors.GraphDepthInvalidCode);
    }

    [Fact]
    public void ADepthPastTheCeiling_IsRefused()
    {
        Create(rootDeviceId: Guid.NewGuid(), depth: TopologyLimits.MaxGraphDepth + 1)
            .IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void ANegativeDepth_IsRefused()
    {
        Create(rootDeviceId: Guid.NewGuid(), depth: -1).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void ADepthOfZero_IsTheRootAlone()
    {
        // A legitimate request rather than a mistake: "draw me this device and nothing else".
        Create(rootDeviceId: Guid.NewGuid(), depth: 0).Value!.Depth.Should().Be(0);
    }

    [Fact]
    public void TheHighestPermittedDepth_IsAccepted()
    {
        Create(rootDeviceId: Guid.NewGuid(), depth: TopologyLimits.MaxGraphDepth)
            .Value!.Depth.Should().Be(TopologyLimits.MaxGraphDepth);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4_095)]
    public void AVlanIdOutsideWhat8021QAdmits_IsRefused(int vlanId)
    {
        Result<TopologyGraphQuery> refused = Create(vlanId: vlanId);

        refused.IsSuccess.Should().BeFalse();
        refused.Error!.Code.Should().Be(TopologyErrors.VlanIdOutOfRangeCode);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4_094)]
    public void AVlanIdInsideIt_IsAccepted(int vlanId)
    {
        Create(vlanId: vlanId).Value!.VlanId.Should().Be(vlanId);
    }
}
