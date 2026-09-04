using System.Net;
using System.Net.Sockets;

using FluentAssertions;

using NetShield.Inventory.Discovery;

using NetShield.Platform.Results;

namespace NetShield.UnitTests.Inventory;

/// <summary>
/// The CIDR arithmetic every discovery seed rests on: what a block holds, what a sweep of it
/// would probe, and how it is cut into jobs.
/// </summary>
public sealed class AddressRangeTests
{
    [Theory]
    [InlineData("10.0.0.0/24", "10.0.0.0/24")]
    [InlineData("10.0.0.5/24", "10.0.0.0/24")]
    [InlineData("10.0.0.5", "10.0.0.5/32")]
    [InlineData("0.0.0.0/0", "0.0.0.0/0")]
    [InlineData("2001:db8::/64", "2001:db8::/64")]
    [InlineData("2001:db8::1", "2001:db8::1/128")]
    public void Parse_NormalisesTheBlock(string written, string stored)
    {
        // Host bits are cleared rather than refused: somebody typing 10.0.0.5/24 means the /24,
        // and two spellings of one block must not both be storable as if they were different.
        AddressRange.Parse(written).Value.ToString().Should().Be(stored);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("lab-sw-01")]
    [InlineData("10.0.0.0/33")]
    [InlineData("10.0.0.0/-1")]
    [InlineData("10.0.0.0/abc")]
    [InlineData("2001:db8::/129")]
    [InlineData("10.0.0.256")]
    public void Parse_SomethingThatIsNotABlock_Fails(string written)
    {
        Result<AddressRange> parsed = AddressRange.Parse(written);

        parsed.IsSuccess.Should().BeFalse();
        parsed.Error!.Code.Should().Be(DiscoveryErrors.InvalidCidrCode);
    }

    [Fact]
    public void Parse_Null_Fails()
    {
        AddressRange.Parse(null).IsSuccess.Should().BeFalse();
    }

    [Theory]
    [InlineData("10.0.0.0/24", 254)]
    [InlineData("10.0.0.0/30", 2)]
    [InlineData("10.0.0.0/31", 2)]
    [InlineData("10.0.0.5/32", 1)]
    [InlineData("10.0.0.0/23", 510)]
    public void HostCount_SkipsTheNetworkAndBroadcastAddressesOfAnIpv4Block(string block, int hosts)
    {
        // A /24 is 254, which is also what the WP-1.6 criterion means by "a run over a /24".
        // Pinging a subnet's broadcast address asks every host on it to answer at once.
        AddressRange.Parse(block).Value.HostCount.Should().Be(hosts);
    }

    [Theory]
    [InlineData("2001:db8::/126", 4)]
    [InlineData("2001:db8::1/128", 1)]
    public void HostCount_SkipsNothingOnAnIpv6Block(string block, int hosts)
    {
        // IPv6 has no broadcast address, so there is nothing to leave out.
        AddressRange.Parse(block).Value.HostCount.Should().Be(hosts);
    }

    [Fact]
    public void HostCount_OfAHugeBlock_SaturatesRatherThanWrapping()
    {
        AddressRange.Parse("::/0").Value.HostCount.Should().Be(long.MaxValue);
    }

    [Theory]
    [InlineData("10.0.0.0/24", "10.0.0.0", true)]
    [InlineData("10.0.0.0/24", "10.0.0.255", true)]
    [InlineData("10.0.0.0/24", "10.0.1.0", false)]
    [InlineData("10.0.0.5/32", "10.0.0.5", true)]
    public void Contains_CoversTheWholeBlockIncludingItsEdges(string block, string address, bool held)
    {
        // Containment is not the same question as "would a sweep probe it": excluding
        // 10.0.0.0/24 means every address in it, including the two a sweep would have skipped.
        AddressRange.Parse(block).Value.Contains(IPAddress.Parse(address)).Should().Be(held);
    }

    [Fact]
    public void Contains_AnAddressOfAnotherFamily_IsFalse()
    {
        AddressRange.Parse("10.0.0.0/8").Value
            .Contains(IPAddress.Parse("2001:db8::1")).Should().BeFalse();
    }

    [Theory]
    [InlineData("10.0.0.0/24", "10.0.0.0/25", true)]
    [InlineData("10.0.0.0/25", "10.0.0.0/24", true)]
    [InlineData("10.0.0.0/25", "10.0.0.128/25", false)]
    [InlineData("10.0.0.0/24", "2001:db8::/64", false)]
    public void Overlaps_IsSymmetricAndFamilyAware(string first, string second, bool overlaps)
    {
        AddressRange left = AddressRange.Parse(first).Value;
        AddressRange right = AddressRange.Parse(second).Value;

        left.Overlaps(right).Should().Be(overlaps);
        right.Overlaps(left).Should().Be(overlaps);
    }

    [Fact]
    public void Spans_ABlockSmallerThanTheCeiling_IsOneSpan()
    {
        IReadOnlyList<AddressSpan> spans = [.. AddressRange.Parse("10.0.0.0/29").Value.Spans(16)];

        spans.Should().ContainSingle();
        spans[0].FirstAddress.ToString().Should().Be("10.0.0.1");
        spans[0].LastAddress.ToString().Should().Be("10.0.0.6");
    }

    [Fact]
    public void Spans_CutTheProbeableAddressesIntoPiecesOfAtMostTheCeiling()
    {
        IReadOnlyList<AddressSpan> spans = [.. AddressRange.Parse("10.0.0.0/24").Value.Spans(100)];

        spans.Should().HaveCount(3);
        spans.Should().AllSatisfy(span => span.Count.Should().BeLessThanOrEqualTo(100));
        spans[0].FirstAddress.ToString().Should().Be("10.0.0.1");
        spans[^1].LastAddress.ToString().Should().Be("10.0.0.254");
        spans.Sum(span => span.Count).Should().Be(254);
    }

    [Fact]
    public void Spans_OfALargerBlock_DoNotSkipTheEdgesOfTheirOwnPieces()
    {
        // The reason a span is not a smaller CIDR block. 10.0.0.255 and 10.0.1.0 are ordinary
        // hosts on a /23; splitting into two /24s would have dropped both.
        IReadOnlyList<AddressSpan> spans = [.. AddressRange.Parse("10.0.0.0/23").Value.Spans(256)];

        spans.Sum(span => span.Count).Should().Be(510);
        spans.Should().Contain(span => span.Contains(IPAddress.Parse("10.0.0.255")));
        spans.Should().Contain(span => span.Contains(IPAddress.Parse("10.0.1.0")));
    }

    [Fact]
    public void Spans_CoverEveryProbeableAddressExactlyOnce()
    {
        List<string> seen = [];

        foreach (AddressSpan span in AddressRange.Parse("10.0.0.0/28").Value.Spans(3))
        {
            for (UInt128 number = span.First; number <= span.Last; number++)
            {
                seen.Add(AddressRange.ToAddress(number, AddressFamily.InterNetwork).ToString());
            }
        }

        seen.Should().OnlyHaveUniqueItems();
        seen.Should().HaveCount(14);
        seen.Should().StartWith("10.0.0.1").And.EndWith("10.0.0.14");
    }

    [Fact]
    public void Spans_ACeilingBelowOne_IsRefused()
    {
        AddressRange range = AddressRange.Parse("10.0.0.0/24").Value;

        Action act = () => range.Spans(0).ToList();

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ToAddress_AndToNumber_RoundTripBothFamilies()
    {
        foreach (string written in (string[])["10.20.30.40", "2001:db8::dead:beef"])
        {
            IPAddress address = IPAddress.Parse(written);

            AddressRange.ToAddress(AddressRange.ToNumber(address), address.AddressFamily)
                .Should().Be(address);
        }
    }
}
