using FluentAssertions;

using NetShield.Inventory.Devices;

namespace NetShield.UnitTests.Inventory;

/// <summary>
/// Tag normalisation. Case and surrounding whitespace are not a caller's mistake to be told
/// about, so they are corrected rather than rejected — which only works if the correction is the
/// same everywhere, including in the filter that has to find what was stored.
/// </summary>
public sealed class DeviceTagsTests
{
    [Fact]
    public void Normalize_Null_ReturnsEmpty() =>
        DeviceTags.Normalize(null).Should().BeEmpty();

    [Fact]
    public void Normalize_MixedCase_LowerCasesEvery() =>
        DeviceTags.Normalize(["Core", "EDGE"]).Should().Equal("core", "edge");

    [Fact]
    public void Normalize_SurroundingWhitespace_IsTrimmed() =>
        DeviceTags.Normalize(["  core  "]).Should().Equal("core");

    [Fact]
    public void Normalize_ValuesDifferingOnlyInCase_CollapseToOne() =>
        DeviceTags.Normalize(["Core", "core", "CORE"]).Should().Equal("core");

    [Fact]
    public void Normalize_EmptyAndWhitespaceEntries_AreDropped() =>
        DeviceTags.Normalize(["core", "", "   "]).Should().Equal("core");

    [Fact]
    public void Normalize_Always_SortsSoTwoEquivalentListsStoreIdentically()
    {
        IReadOnlyList<string> typedOneWay = DeviceTags.Normalize(["wan", "core", "edge"]);
        IReadOnlyList<string> typedAnother = DeviceTags.Normalize(["edge", "wan", "core"]);

        typedOneWay.Should().Equal("core", "edge", "wan");
        typedOneWay.Should().Equal(typedAnother);
    }
}
