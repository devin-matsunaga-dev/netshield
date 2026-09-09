using System.Globalization;

using FluentAssertions;

using NetShield.Inventory.Topology.Handlers;

using NetShield.Platform.Paging;
using NetShield.Platform.Results;

namespace NetShield.UnitTests.Inventory;

/// <summary>
/// The keyset position a graph page resumes from, and the ordering it means.
/// </summary>
/// <remarks>
/// "Paginated by subgraph" comes to this triple: the island, the distance from that island's
/// root, and the device. A graph has no natural row order, so the ordering the layout produces is
/// the only thing a cursor can name.
/// </remarks>
public sealed class TopologyGraphCursorTests
{
    private static Guid Device(int index) =>
        new($"00000000-0000-0000-0000-{index:D12}");

    private static string Encoded(int component, int rank, Guid deviceId) =>
        Cursor.Encode(TopologyGraphCursor.Compose(component, rank, deviceId));

    [Fact]
    public void APositionRoundTripsThroughAnEncodedCursor()
    {
        Result<TopologyGraphCursor> decoded =
            TopologyGraphCursor.Decode(Encoded(2, 3, Device(7)));

        decoded.Value!.ComponentIndex.Should().Be(2);
        decoded.Value.Rank.Should().Be(3);
        decoded.Value.DeviceId.Should().Be(Device(7));
    }

    [Fact]
    public void TheFirstPositionOfAGraphRoundTrips()
    {
        TopologyGraphCursor.Decode(Encoded(0, 0, Guid.Empty))
            .IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void ACursorThisEndpointDidNotIssue_IsRefused()
    {
        Result<TopologyGraphCursor> decoded = TopologyGraphCursor.Decode(Cursor.Encode("2:3"));

        decoded.IsSuccess.Should().BeFalse();
        decoded.Error!.Code.Should().Be(Cursor.InvalidCursorCode);
    }

    [Fact]
    public void ACursorNamingSomethingThatIsNotADevice_IsRefused()
    {
        TopologyGraphCursor.Decode(Cursor.Encode("0:0:sw-core"))
            .IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void ANegativePosition_IsRefused()
    {
        // Not a position this endpoint composes, so it is a cursor somebody wrote by hand.
        TopologyGraphCursor.Decode(Cursor.Encode($"-1:0:{Device(1):D}"))
            .IsSuccess.Should().BeFalse();

        TopologyGraphCursor.Decode(Cursor.Encode($"0:-1:{Device(1):D}"))
            .IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void AValueThatIsNotEvenEncoded_IsRefused()
    {
        TopologyGraphCursor.Decode("!!!").IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void ACursorIsIndependentOfCulture()
    {
        // The parts are bare integers and a GUID, and have to be composed and read the same way
        // on a machine whose culture formats numbers differently.
        CultureInfo original = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            TopologyGraphCursor.Decode(Encoded(1, 2, Device(3)))
                .Value!.Rank.Should().Be(2);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    // --- the ordering the cursor names --------------------------------------------------------

    [Fact]
    public void ALaterComponent_FollowsAnEarlierOne()
    {
        TopologyGraphCursor position = new(0, 5, Device(9));

        position.Precedes(1, 0, Device(1)).Should().BeTrue();
    }

    [Fact]
    public void AnEarlierComponent_DoesNotFollow()
    {
        TopologyGraphCursor position = new(1, 0, Device(1));

        position.Precedes(0, 9, Device(9)).Should().BeFalse();
    }

    [Fact]
    public void AHigherRankInTheSameComponent_Follows()
    {
        TopologyGraphCursor position = new(0, 1, Device(9));

        position.Precedes(0, 2, Device(1)).Should().BeTrue();
        position.Precedes(0, 0, Device(9)).Should().BeFalse();
    }

    [Fact]
    public void AHigherIdAtTheSameRank_Follows()
    {
        TopologyGraphCursor position = new(0, 1, Device(5));

        position.Precedes(0, 1, Device(6)).Should().BeTrue();
        position.Precedes(0, 1, Device(4)).Should().BeFalse();
    }

    [Fact]
    public void ThePositionItself_DoesNotFollow()
    {
        // Or the last node of a page would be the first node of the next one.
        TopologyGraphCursor position = new(1, 2, Device(3));

        position.Precedes(1, 2, Device(3)).Should().BeFalse();
    }
}
