using FluentAssertions;

using NetShield.Inventory.Topology.Handlers;

using NetShield.Platform.Paging;
using NetShield.Platform.Results;

namespace NetShield.UnitTests.Inventory;

/// <summary>The port page's keyset position: the device's own <c>ifIndex</c>.</summary>
public sealed class DevicePortCursorTests
{
    [Fact]
    public void Decode_OfAComposedPosition_ReadsTheIndexBack()
    {
        string cursor = Cursor.Encode(DevicePortCursor.Compose(10101));

        Result<DevicePortCursor> decoded = DevicePortCursor.Decode(cursor);

        decoded.IsSuccess.Should().BeTrue();
        decoded.Value.IfIndex.Should().Be(10101);
    }

    [Fact]
    public void Decode_OfSomethingThatIsNotACursor_IsRefused()
    {
        DevicePortCursor.Decode("not-a-cursor").IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Decode_OfACursorCarryingSomethingOtherThanANumber_IsRefused()
    {
        // A well-formed cursor from another endpoint. It decodes and then means nothing here,
        // which is a refusal rather than an exception.
        DevicePortCursor.Decode(Cursor.Encode("00000000-0000-0000-0000-000000000001"))
            .IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Compose_WritesThePlainPosition_SoItIsEncodedExactlyOnce()
    {
        // A position that arrived already encoded would be encoded twice by ToCursorPage and
        // never decode. The same trap DeviceInterfaceCursor documents.
        DevicePortCursor.Compose(7).Should().Be("7");
    }
}
