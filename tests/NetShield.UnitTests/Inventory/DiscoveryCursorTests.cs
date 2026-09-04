using FluentAssertions;

using NetShield.Inventory.Discovery.Handlers;

using NetShield.Platform.Paging;
using NetShield.Platform.Results;

namespace NetShield.UnitTests.Inventory;

/// <summary>
/// The keyset position the discovery lists page by. A cursor that does not round-trip exactly is
/// a page boundary that repeats a row or skips one.
/// </summary>
public sealed class DiscoveryCursorTests
{
    [Fact]
    public void APositionRoundTrips()
    {
        DateTimeOffset timestamp = new(2026, 9, 4, 11, 22, 33, 456, TimeSpan.Zero);
        Guid id = Guid.CreateVersion7();

        Result<DiscoveryCursor> decoded =
            DiscoveryCursor.Decode(Cursor.Encode(DiscoveryCursor.Compose(timestamp, id)));

        decoded.IsSuccess.Should().BeTrue();
        decoded.Value.Timestamp.Should().Be(timestamp);
        decoded.Value.Id.Should().Be(id);
    }

    [Fact]
    public void APositionRoundTripsWithoutLosingATick()
    {
        // Two rows written in the same millisecond are a real page boundary at the rate a sweep
        // applies responders.
        DateTimeOffset timestamp =
            new DateTimeOffset(2026, 9, 4, 0, 0, 0, TimeSpan.Zero).AddTicks(1234567);

        DiscoveryCursor.Decode(Cursor.Encode(DiscoveryCursor.Compose(timestamp, Guid.Empty)))
            .Value.Timestamp.Should().Be(timestamp);
    }

    [Fact]
    public void ALocalTimestampIsReadBackAsUtc()
    {
        DateTimeOffset local = new(2026, 9, 4, 12, 0, 0, TimeSpan.FromHours(5));

        DiscoveryCursor.Decode(Cursor.Encode(DiscoveryCursor.Compose(local, Guid.Empty)))
            .Value.Timestamp.Should().Be(local.ToUniversalTime());
    }

    [Theory]
    [InlineData("not-base64url-!!")]
    [InlineData("")]
    public void SomethingThatIsNotACursor_IsARejection(string cursor)
    {
        Result<DiscoveryCursor> decoded = DiscoveryCursor.Decode(cursor);

        decoded.IsSuccess.Should().BeFalse();
        decoded.Error!.Code.Should().Be(Cursor.InvalidCursorCode);
    }

    [Theory]
    [InlineData("only-one-half")]
    [InlineData("2026-09-04T00:00:00.0000000+00:00not-a-guid")]
    [InlineData("not-a-timestamp00000000-0000-0000-0000-000000000000")]
    public void ACursorThisEndpointDidNotIssue_IsARejection(string position)
    {
        DiscoveryCursor.Decode(Cursor.Encode(position)).IsSuccess.Should().BeFalse();
    }
}
