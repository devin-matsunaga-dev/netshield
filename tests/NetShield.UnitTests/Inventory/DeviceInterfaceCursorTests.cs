using FluentAssertions;

using NetShield.Inventory.Discovery.Handlers;

using NetShield.Platform.Paging;
using NetShield.Platform.Results;

namespace NetShield.UnitTests.Inventory;

/// <summary>
/// The keyset position an interface page resumes from.
/// </summary>
/// <remarks>
/// Its own cursor rather than <c>DiscoveryCursor</c>'s timestamp-and-id, because an interface
/// list is ordered by the device's own <c>ifIndex</c> — the order the device presents its ports
/// in — and that index is unique per device, so it is a whole position on its own.
/// </remarks>
public sealed class DeviceInterfaceCursorTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(10_101)]
    [InlineData(int.MaxValue)]
    public void Decode_WhatComposeProduced_ReturnsTheSameIndex(int ifIndex)
    {
        string encoded = Cursor.Encode(DeviceInterfaceCursor.Compose(ifIndex));

        Result<DeviceInterfaceCursor> decoded = DeviceInterfaceCursor.Decode(encoded);

        decoded.IsSuccess.Should().BeTrue();
        decoded.Value.IfIndex.Should().Be(ifIndex);
    }

    [Fact]
    public void Decode_SomethingThisEndpointDidNotIssue_IsRejected()
    {
        DeviceInterfaceCursor.Decode(Cursor.Encode("not-a-number")).IsSuccess.Should().BeFalse();
    }

    /// <summary>
    /// A caller who edits the cursor gets a refusal rather than a page starting somewhere
    /// arbitrary — the same guard every other cursor in the system has.
    /// </summary>
    [Fact]
    public void Decode_SomethingThatIsNotEncodedAtAll_IsRejected()
    {
        Result<DeviceInterfaceCursor> decoded = DeviceInterfaceCursor.Decode("12");

        decoded.IsSuccess.Should().BeFalse();
        decoded.Error!.Code.Should().Be(Cursor.InvalidCursorCode);
    }
}
