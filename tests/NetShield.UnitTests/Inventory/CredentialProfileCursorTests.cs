using FluentAssertions;

using NetShield.Contracts.Inventory;

using NetShield.Inventory.Credentials;
using NetShield.Inventory.Credentials.Handlers;

using NetShield.Platform.Paging;
using NetShield.Platform.Results;

namespace NetShield.UnitTests.Inventory;

/// <summary>
/// The keyset position a credential profile page resumes from. Same construction as the device
/// cursor, and the same properties have to hold.
/// </summary>
public sealed class CredentialProfileCursorTests
{
    [Fact]
    public void PositionOf_ByName_CarriesTheNameAndTheId()
    {
        CredentialProfile profile = ProfileNamed("Core read-only");

        string position = CredentialProfileCursor.PositionOf(profile, CredentialProfileSortField.Name);

        CredentialProfileCursor decoded = Decode(position);

        decoded.SortValue.Should().Be("Core read-only");
        decoded.Id.Should().Be(profile.Id);
    }

    [Fact]
    public void PositionOf_ByCreatedAt_RoundTripsTheTimestampWithoutLosingATick()
    {
        CredentialProfile profile = ProfileNamed("Core");

        string position = CredentialProfileCursor.PositionOf(profile, CredentialProfileSortField.CreatedAt);

        Decode(position).TryReadTimestamp(out DateTimeOffset createdAt).Should().BeTrue();
        createdAt.Should().Be(profile.CreatedAt);
    }

    /// <summary>
    /// The id is always part of the position. A soft-deleted profile releases its name, so two
    /// rows can hold one, and a page boundary between them would otherwise repeat or skip a row.
    /// </summary>
    [Fact]
    public void PositionOf_ForTwoProfilesOfTheSameName_ProducesDifferentPositions()
    {
        CredentialProfile first = ProfileNamed("Core");
        CredentialProfile second = ProfileNamed("Core");

        CredentialProfileCursor.PositionOf(first, CredentialProfileSortField.Name)
            .Should().NotBe(CredentialProfileCursor.PositionOf(second, CredentialProfileSortField.Name));
    }

    [Theory]
    [InlineData("not-base64-!!")]
    [InlineData("")]
    public void Decode_OfSomethingThisEndpointDidNotIssue_IsRefused(string cursor) =>
        CredentialProfileCursor.Decode(cursor).IsSuccess.Should().BeFalse();

    [Fact]
    public void Decode_OfAWellFormedCursorWithNoId_IsRefused()
    {
        Result<CredentialProfileCursor> decoded =
            CredentialProfileCursor.Decode(Cursor.Encode("just-a-name"));

        decoded.IsSuccess.Should().BeFalse();
        decoded.Error!.Code.Should().Be(Cursor.InvalidCursorCode);
    }

    /// <summary>The cursor is opaque on the wire: the endpoint encodes what the position composes.</summary>
    private static CredentialProfileCursor Decode(string position) =>
        CredentialProfileCursor.Decode(Cursor.Encode(position)).Value;

    private static CredentialProfile ProfileNamed(string name) => new()
    {
        Id = Guid.CreateVersion7(),
        Name = name,
        NormalizedName = name.ToLowerInvariant(),
        Kind = CredentialKind.SnmpV2c,
        KeyId = "test",
        WrappedDataKey = [],
        MaterialCiphertext = [],
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        MaterialUpdatedAt = DateTimeOffset.UtcNow
    };
}
