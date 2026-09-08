using FluentAssertions;

using NetShield.Inventory.Clients;

namespace NetShield.UnitTests.Inventory;

/// <summary>
/// Normalising a hardware address, and reading the three facts its first octet carries.
/// </summary>
/// <remarks>
/// Small, and load-bearing out of proportion to its size. A client is keyed by its MAC and by
/// nothing else, so an address that normalises two ways is two clients — and the two would each
/// hold half of one endpoint's history, which is exactly the kind of wrongness nobody notices
/// until an attribution is questioned months later.
/// </remarks>
public sealed class MacAddressTests
{
    [Theory]
    [InlineData("AA:BB:CC:DD:EE:FF")]
    [InlineData("aa:bb:cc:dd:ee:ff")]
    [InlineData("AA-BB-CC-DD-EE-FF")]
    [InlineData("aabb.ccdd.eeff")]
    [InlineData("AABBCCDDEEFF")]
    [InlineData(" aa bb cc dd ee ff ")]
    public void Normalize_WithAnyOrdinarySpelling_ProducesOneValue(string spelling)
    {
        // Colons, hyphens, Cisco's dotted quads, the bare twelve characters and a spaced form all
        // mean one address. Every non-hexadecimal character is a separator, which is what lets
        // one parser cover all of them.
        MacAddress.Normalize(spelling).Should().Be("AA:BB:CC:DD:EE:FF");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("AA:BB:CC:DD:EE")]
    [InlineData("AA:BB:CC:DD:EE:F")]
    [InlineData("AA:BB:CC:DD:EE:FF:00")]
    [InlineData("not-an-address")]
    public void Normalize_WithTheWrongNumberOfDigits_IsRefused(string? value)
    {
        // Deliberately not "keep the first twelve hex characters". An eleven-digit string is a
        // corrupted address rather than one worth guessing at, and guessing would attribute an
        // observation to a host that does not exist.
        MacAddress.Normalize(value).Should().BeNull();
    }

    [Fact]
    public void Normalize_WithMoreHexThanAnAddressHas_StopsRatherThanTruncating()
    {
        MacAddress.Normalize("AABBCCDDEEFF00112233").Should().BeNull();
    }

    [Fact]
    public void Oui_IsTheFirstThreeOctets()
    {
        MacAddress.Oui("AA:BB:CC:DD:EE:FF").Should().Be("AA:BB:CC");
    }

    [Theory]
    [InlineData("02:00:00:00:00:01", true)]
    [InlineData("06:00:00:00:00:01", true)]
    [InlineData("00:1A:2B:00:00:01", false)]
    [InlineData("AA:BB:CC:00:00:01", true)]
    public void IsLocallyAdministered_ReadsTheUniversalLocalBit(string mac, bool expected)
    {
        // Bit 1 of the first octet. A locally administered address stands for no vendor at all,
        // which is what MAC randomisation on a phone produces — so an unresolvable OUI is a
        // deliberate absence rather than a gap in a registry.
        MacAddress.IsLocallyAdministered(mac).Should().Be(expected);
    }

    [Theory]
    [InlineData("01:00:5E:00:00:01", true)]
    [InlineData("FF:FF:FF:FF:FF:FF", true)]
    [InlineData("00:1A:2B:00:00:01", false)]
    [InlineData("AA:BB:CC:00:00:01", false)]
    public void IsMulticast_ReadsTheGroupBit(string mac, bool expected)
    {
        MacAddress.IsMulticast(mac).Should().Be(expected);
    }
}
