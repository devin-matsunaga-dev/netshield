using FluentAssertions;

using NetShield.Contracts.Paging;

using NetShield.Platform.Paging;
using NetShield.Platform.Results;

namespace NetShield.UnitTests.Platform;

/// <summary>
/// Covers the cursor pagination every list endpoint uses (CONVENTIONS.md §4).
/// </summary>
public sealed class CursorPagingTests
{
    [Fact]
    public void Cursor_RoundTrips_AKeysetPosition()
    {
        string encoded = Cursor.Encode("2026-09-03T10:00:00Z|0199...");

        Cursor.Decode(encoded).Value.Should().Be("2026-09-03T10:00:00Z|0199...");
    }

    [Fact]
    public void Cursor_IsUrlSafe_SoItSurvivesAQueryString()
    {
        string encoded = Cursor.Encode("a|b/c+d?e=f");

        encoded.Should().NotContain("+").And.NotContain("/").And.NotContain("=");
    }

    [Theory]
    [InlineData("not a cursor!")]
    [InlineData("")]
    [InlineData("   ")]
    public void Cursor_Decode_RejectsSomethingThisEndpointNeverIssued(string cursor)
    {
        Result<string> decoded = Cursor.Decode(cursor);

        decoded.IsSuccess.Should().BeFalse();
        decoded.Error!.Kind.Should().Be(ErrorKind.Validation);
        decoded.Error.Code.Should().Be(Cursor.InvalidCursorCode);
    }

    [Fact]
    public void PageRequest_DefaultsToFiftyRows()
    {
        PageRequest request = PageRequest.Create(cursor: null, limit: null).Value;

        request.Limit.Should().Be(50);
        request.Cursor.Should().BeNull();
    }

    [Fact]
    public void PageRequest_FetchesOneMoreRowThanAsked_SoTheNextCursorNeedsNoSecondQuery()
    {
        PageRequest.Create(cursor: null, limit: 10).Value.FetchLimit.Should().Be(11);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(201)]
    public void PageRequest_RejectsALimitOutsideTheAllowedRange(int limit)
    {
        Result<PageRequest> request = PageRequest.Create(cursor: null, limit);

        request.IsSuccess.Should().BeFalse();
        request.Error!.Code.Should().Be(PageRequest.InvalidLimitCode);
        request.Error.Failures.Should().ContainKey("limit");
    }

    [Fact]
    public void PageRequest_AcceptsTheMaximum()
    {
        PageRequest.Create(cursor: null, PageRequest.MaxLimit).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void PageRequest_TreatsAnEmptyCursorAsNoCursor()
    {
        PageRequest.Create("   ", limit: null).Value.Cursor.Should().BeNull();
    }

    [Fact]
    public void ToCursorPage_IssuesNoCursor_WhenTheRowsRanOut()
    {
        PageRequest request = PageRequest.Create(cursor: null, limit: 3).Value;

        CursorPage<int> page = new[] { 1, 2 }.ToCursorPage(request, row => row.ToString());

        page.Items.Should().Equal(1, 2);
        page.NextCursor.Should().BeNull();
    }

    [Fact]
    public void ToCursorPage_DropsTheExtraRow_AndTurnsItIntoTheNextCursor()
    {
        PageRequest request = PageRequest.Create(cursor: null, limit: 3).Value;

        // Four rows came back for a page of three: the fourth exists only to prove there is more.
        CursorPage<int> page = new[] { 1, 2, 3, 4 }.ToCursorPage(request, row => row.ToString());

        page.Items.Should().Equal(1, 2, 3);
        page.NextCursor.Should().NotBeNull();
        Cursor.Decode(page.NextCursor!).Value.Should().Be("3", "the cursor points at the last row served");
    }

    [Fact]
    public void ToCursorPage_CarriesATotalCount_WhenTheEndpointSuppliesOne()
    {
        PageRequest request = PageRequest.Create(cursor: null, limit: 2).Value;

        new[] { 1 }.ToCursorPage(request, row => row.ToString(), totalCount: 1).TotalCount.Should().Be(1);
    }
}
