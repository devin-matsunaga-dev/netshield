using FluentAssertions;

using NetShield.Inventory.Topology.Handlers;

using NetShield.Platform.Paging;
using NetShield.Platform.Results;

namespace NetShield.UnitTests.Inventory;

/// <summary>The keyset position a VLAN page resumes from.</summary>
public sealed class VlanCursorTests
{
    [Fact]
    public void APositionRoundTripsThroughAnEncodedCursor()
    {
        VlanCursor.Decode(Cursor.Encode(VlanCursor.Compose(20)))
            .Value!.VlanId.Should().Be(20);
    }

    [Fact]
    public void TheHighestVlanIdRoundTrips()
    {
        VlanCursor.Decode(Cursor.Encode(VlanCursor.Compose(4_094)))
            .Value!.VlanId.Should().Be(4_094);
    }

    [Fact]
    public void ACursorThisEndpointDidNotIssue_IsRefused()
    {
        Result<VlanCursor> decoded = VlanCursor.Decode(Cursor.Encode("not-a-vlan-id"));

        decoded.IsSuccess.Should().BeFalse();
        decoded.Error!.Code.Should().Be(Cursor.InvalidCursorCode);
    }

    [Fact]
    public void AValueThatIsNotEvenEncoded_IsRefused()
    {
        VlanCursor.Decode("!!!").IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void ACursorIsIndependentOfCulture()
    {
        // A VLAN id is a bare integer and has to be composed and read the same way on a machine
        // whose culture would group it — otherwise a cursor issued on one host is unreadable on
        // another.
        VlanCursor.Compose(1_234).Should().Be("1234");
    }
}
