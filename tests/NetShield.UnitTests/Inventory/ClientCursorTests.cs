using FluentAssertions;

using NetShield.Inventory.Clients.Handlers;

using NetShield.Platform.Paging;
using NetShield.Platform.Results;

namespace NetShield.UnitTests.Inventory;

/// <summary>
/// The two keyset positions the client screens page by.
/// </summary>
/// <remarks>
/// Both carry an id beside their timestamp, and both have to. One walk stamps every client it
/// confirmed and every interval it opened with the same instant, so a page boundary falling
/// inside one walk's worth of rows would repeat a row or skip one if the timestamp were the whole
/// position.
/// </remarks>
public sealed class ClientCursorTests
{
    private static readonly DateTimeOffset Instant =
        new(2026, 9, 8, 11, 30, 15, 123, TimeSpan.Zero);

    [Fact]
    public void ClientCursor_Decode_WhatComposeProduced_ReturnsTheSamePosition()
    {
        Guid id = Guid.CreateVersion7(Instant);

        Result<ClientCursor> decoded =
            ClientCursor.Decode(Cursor.Encode(ClientCursor.Compose(Instant, id)));

        decoded.IsSuccess.Should().BeTrue();
        decoded.Value.LastSeenAt.Should().Be(Instant);
        decoded.Value.Id.Should().Be(id);
    }

    [Fact]
    public void ClientCursor_Decode_KeepsSubSecondPrecision()
    {
        // The round-trip format is "O". A cursor that lost a millisecond would resume a page
        // inside the rows it had already served.
        DateTimeOffset precise = Instant.AddTicks(4_567);
        Guid id = Guid.CreateVersion7(precise);

        Result<ClientCursor> decoded =
            ClientCursor.Decode(Cursor.Encode(ClientCursor.Compose(precise, id)));

        decoded.Value.LastSeenAt.Should().Be(precise);
    }

    [Fact]
    public void ClientCursor_Decode_ANonUtcInstant_ReturnsItAsUtc()
    {
        // Composed from the UTC form, so a page cursor issued in one offset and read in another
        // resumes at the same row (CONVENTIONS.md §3: never a local time).
        DateTimeOffset local = new(2026, 9, 8, 13, 30, 15, TimeSpan.FromHours(2));

        Result<ClientCursor> decoded = ClientCursor.Decode(
            Cursor.Encode(ClientCursor.Compose(local, Guid.CreateVersion7(local))));

        decoded.Value.LastSeenAt.Should().Be(local.ToUniversalTime());
        decoded.Value.LastSeenAt.Offset.Should().Be(TimeSpan.Zero);
    }

    [Theory]
    [InlineData("not-a-cursor")]
    [InlineData("2026-09-08T11:30:15.0000000Z")]
    public void ClientCursor_Decode_SomethingThisEndpointDidNotIssue_IsRejected(string plain)
    {
        Result<ClientCursor> decoded = ClientCursor.Decode(Cursor.Encode(plain));

        decoded.IsSuccess.Should().BeFalse();
        decoded.Error!.Code.Should().Be(Cursor.InvalidCursorCode);
    }

    [Fact]
    public void ClientCursor_Decode_SomethingThatIsNotEncodedAtAll_IsRejected()
    {
        ClientCursor.Decode("2026-09-08").IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void ClientBindingCursor_Decode_WhatComposeProduced_ReturnsTheSamePosition()
    {
        Guid id = Guid.CreateVersion7(Instant);

        Result<ClientBindingCursor> decoded = ClientBindingCursor.Decode(
            Cursor.Encode(ClientBindingCursor.Compose(Instant, id)));

        decoded.IsSuccess.Should().BeTrue();
        decoded.Value.ObservedFrom.Should().Be(Instant);
        decoded.Value.Id.Should().Be(id);
    }

    [Fact]
    public void ClientBindingCursor_Decode_SomethingThisEndpointDidNotIssue_IsRejected()
    {
        Result<ClientBindingCursor> decoded = ClientBindingCursor.Decode(Cursor.Encode("13"));

        decoded.IsSuccess.Should().BeFalse();
        decoded.Error!.Code.Should().Be(Cursor.InvalidCursorCode);
    }
}
