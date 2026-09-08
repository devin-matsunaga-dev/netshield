using FluentAssertions;

using NetShield.Inventory.Topology.Handlers;

using NetShield.Platform.Paging;
using NetShield.Platform.Results;

namespace NetShield.UnitTests.Inventory;

/// <summary>The keyset position an adjacency page resumes from.</summary>
public sealed class AdjacencyCursorTests
{
    [Fact]
    public void APositionRoundTripsThroughAnEncodedCursor()
    {
        Guid id = Guid.CreateVersion7();

        AdjacencyCursor.Decode(Cursor.Encode(AdjacencyCursor.Compose(id)))
            .Value!.Id.Should().Be(id);
    }

    [Fact]
    public void ACursorThisEndpointDidNotIssue_IsRefused()
    {
        Result<AdjacencyCursor> decoded = AdjacencyCursor.Decode(Cursor.Encode("not-a-guid"));

        decoded.IsSuccess.Should().BeFalse();
        decoded.Error!.Code.Should().Be(Cursor.InvalidCursorCode);
    }

    [Fact]
    public void AValueThatIsNotEvenEncoded_IsRefused()
    {
        AdjacencyCursor.Decode("!!!").IsSuccess.Should().BeFalse();
    }
}
