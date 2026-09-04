using System.Net;

using FluentAssertions;

using NetShield.Inventory.Devices;
using NetShield.Inventory.Devices.Handlers;

using NetShield.Platform.Paging;
using NetShield.Platform.Results;

namespace NetShield.UnitTests.Inventory;

/// <summary>
/// The device list's keyset cursor. It is opaque to a caller, which means nothing outside this
/// type needs to understand it — and that only holds if a cursor it did not issue is refused
/// rather than half-read.
/// </summary>
/// <remarks>
/// Every round trip here goes through <see cref="Cursor.Encode"/> the way the endpoint does,
/// rather than through a helper of the test's own. An earlier version of this type encoded the
/// position itself as well, so a cursor was base64 twice and the second page of every list was a
/// 400 — a test that encoded the same wrong way would have agreed with it.
/// </remarks>
public sealed class DeviceCursorTests
{
    [Fact]
    public void Decode_ACursorTheEndpointIssued_ReturnsThePosition()
    {
        Guid id = Guid.CreateVersion7();

        Result<DeviceCursor> decoded = Decode(DeviceCursor.Compose("core-sw-01", id));

        decoded.IsSuccess.Should().BeTrue();
        decoded.Value.SortValue.Should().Be("core-sw-01");
        decoded.Value.Id.Should().Be(id);
    }

    [Fact]
    public void Decode_AHostnameHoldingPunctuation_StillRoundTrips()
    {
        // A hostname cannot hold a unit separator, but the assertion is what says so: if the
        // separator is ever changed to something a hostname can contain, this is what fails.
        Result<DeviceCursor> decoded =
            Decode(DeviceCursor.Compose("edge-fw-01.corp.example", Guid.CreateVersion7()));

        decoded.IsSuccess.Should().BeTrue();
        decoded.Value.SortValue.Should().Be("edge-fw-01.corp.example");
    }

    [Fact]
    public void PositionOf_SortedByCreation_ReadsBackToTheSameInstant()
    {
        DateTimeOffset created = new(2026, 9, 4, 11, 22, 33, 444, TimeSpan.Zero);

        Result<DeviceCursor> decoded = Decode(
            DeviceCursor.PositionOf(DeviceAt(created), DeviceSortField.CreatedAt));

        decoded.IsSuccess.Should().BeTrue();
        decoded.Value.TryReadTimestamp(out DateTimeOffset read).Should().BeTrue();
        read.Should().Be(created);
    }

    [Fact]
    public void PositionOf_SortedByHostname_CarriesTheHostnameAndTheId()
    {
        Device device = DeviceAt(DateTimeOffset.UtcNow);

        Result<DeviceCursor> decoded = Decode(
            DeviceCursor.PositionOf(device, DeviceSortField.Hostname));

        decoded.Value.SortValue.Should().Be(device.Hostname);
        decoded.Value.Id.Should().Be(device.Id);
    }

    [Theory]
    [InlineData("not-base64!!")]
    [InlineData("")]
    public void Decode_SomethingThatIsNotACursor_IsRefused(string cursor)
    {
        Result<DeviceCursor> decoded = DeviceCursor.Decode(cursor);

        decoded.IsSuccess.Should().BeFalse();
        decoded.Error!.Code.Should().Be(Cursor.InvalidCursorCode);
    }

    [Fact]
    public void Decode_AWellFormedCursorWithNoId_IsRefused()
    {
        Result<DeviceCursor> decoded = DeviceCursor.Decode(Cursor.Encode("core-sw-01"));

        decoded.IsSuccess.Should().BeFalse();
        decoded.Error!.Code.Should().Be(Cursor.InvalidCursorCode);
    }

    /// <summary>
    /// The double-encoding bug, pinned. A position that has already been through
    /// <see cref="Cursor.Encode"/> must not decode into a position.
    /// </summary>
    [Fact]
    public void Decode_APositionEncodedTwice_IsRefused()
    {
        string once = Cursor.Encode(DeviceCursor.Compose("core-sw-01", Guid.CreateVersion7()));

        DeviceCursor.Decode(Cursor.Encode(once)).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void TryReadTimestamp_APositionThatIsAHostname_Fails() =>
        new DeviceCursor("core-sw-01", Guid.CreateVersion7())
            .TryReadTimestamp(out _).Should().BeFalse();

    /// <summary>Encodes a position the way the endpoint does, then reads it back.</summary>
    private static Result<DeviceCursor> Decode(string position) =>
        DeviceCursor.Decode(Cursor.Encode(position));

    private static Device DeviceAt(DateTimeOffset created) => new()
    {
        Id = Guid.CreateVersion7(created),
        Hostname = "core-sw-01",
        PrimaryIpAddress = IPAddress.Parse("10.0.0.1"),
        CreatedAt = created
    };
}
