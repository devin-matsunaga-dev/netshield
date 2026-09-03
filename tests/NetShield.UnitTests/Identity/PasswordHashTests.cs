using FluentAssertions;

using NetShield.Identity.Passwords;

namespace NetShield.UnitTests.Identity;

/// <summary>
/// The PHC string is what makes a stored hash self-describing, and a hash that cannot be read
/// back exactly as it was written is a password nobody can ever verify again.
/// </summary>
public sealed class PasswordHashTests
{
    [Fact]
    public void Format_ThenParse_RoundTripsEveryField()
    {
        PasswordHash original = new(19456, 2, 1, [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16], [9, 8, 7, 6]);

        PasswordHash.TryParse(original.Format(), out PasswordHash? parsed).Should().BeTrue();

        parsed.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void Format_WritesThePhcStringForArgon2id()
    {
        PasswordHash hash = new(19456, 2, 1, [0xAA, 0xBB], [0xCC, 0xDD]);

        hash.Format().Should().StartWith("$argon2id$v=19$m=19456,t=2,p=1$");
    }

    [Fact]
    public void Format_WritesTheSaltAndDigestAsUnpaddedBase64()
    {
        // One byte each, so the padded encoding would carry two '=' characters apiece.
        PasswordHash hash = new(19456, 2, 1, [0xAA], [0xBB]);

        string[] fields = hash.Format().Split('$');

        fields[^2].Should().NotContain("=", "the PHC format specifies unpadded base64");
        fields[^1].Should().NotContain("=");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a hash at all")]
    [InlineData("$argon2i$v=19$m=19456,t=2,p=1$YWJjZA$ZWZnaA")]
    [InlineData("$argon2id$v=16$m=19456,t=2,p=1$YWJjZA$ZWZnaA")]
    [InlineData("$argon2id$v=19$m=19456,t=2$YWJjZA$ZWZnaA")]
    [InlineData("$argon2id$v=19$m=0,t=2,p=1$YWJjZA$ZWZnaA")]
    [InlineData("$argon2id$v=19$m=19456,t=2,p=1$!!!!$ZWZnaA")]
    [InlineData("$argon2id$v=19$m=19456,t=2,p=1$YWJjZA")]
    public void TryParse_Rejects_AnythingThatIsNotOneOfOurHashes(string? encoded)
    {
        PasswordHash.TryParse(encoded, out PasswordHash? parsed).Should().BeFalse();

        parsed.Should().BeNull();
    }
}
