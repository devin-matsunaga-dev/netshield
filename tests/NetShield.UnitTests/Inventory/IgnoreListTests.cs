using System.Net;

using FluentAssertions;

using NetShield.Inventory.Discovery;

namespace NetShield.UnitTests.Inventory;

/// <summary>
/// The containment test that decides whether a responder becomes a candidate. It is address-in-
/// block, which is PostgreSQL's <c>&lt;&lt;=</c> and not something EF can express — so it happens
/// here, over a list read once per sweep result.
/// </summary>
public sealed class IgnoreListTests
{
    [Fact]
    public void Empty_HoldsNothing()
    {
        IgnoreList.Empty.Contains(IPAddress.Parse("10.0.0.1")).Should().BeFalse();
    }

    [Fact]
    public void AnAddressInsideAnIgnoredBlock_IsIgnored()
    {
        IgnoreList.From(["10.0.0.0/24"])
            .Contains(IPAddress.Parse("10.0.0.77")).Should().BeTrue();
    }

    [Fact]
    public void AnAddressOutsideEveryIgnoredBlock_IsNot()
    {
        IgnoreList.From(["10.0.0.0/24"])
            .Contains(IPAddress.Parse("10.0.1.1")).Should().BeFalse();
    }

    [Fact]
    public void AnIgnoredSingleAddress_IsIgnoredAndItsNeighbourIsNot()
    {
        IgnoreList ignores = IgnoreList.From(["10.0.0.5/32"]);

        ignores.Contains(IPAddress.Parse("10.0.0.5")).Should().BeTrue();
        ignores.Contains(IPAddress.Parse("10.0.0.6")).Should().BeFalse();
    }

    [Fact]
    public void TheNetworkAndBroadcastAddressesOfAnIgnoredBlockAreIgnoredToo()
    {
        // Containment covers the whole block, not the addresses a sweep would have probed.
        IgnoreList ignores = IgnoreList.From(["10.0.0.0/24"]);

        ignores.Contains(IPAddress.Parse("10.0.0.0")).Should().BeTrue();
        ignores.Contains(IPAddress.Parse("10.0.0.255")).Should().BeTrue();
    }

    [Fact]
    public void AnEntryThatWillNotParse_IsSkippedRatherThanThrownOn()
    {
        // The column is only ever written through AddressRange.Parse, so a malformed row means
        // somebody edited the database by hand — and losing a whole sweep result over it would
        // cost more than it saves.
        IgnoreList ignores = IgnoreList.From(["not-a-block", "10.0.0.0/24"]);

        ignores.Contains(IPAddress.Parse("10.0.0.1")).Should().BeTrue();
    }

    [Fact]
    public void AnIpv6AddressIsNotMatchedByAnIpv4Block()
    {
        IgnoreList.From(["0.0.0.0/0"])
            .Contains(IPAddress.Parse("2001:db8::1")).Should().BeFalse();
    }
}
