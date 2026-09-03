using FluentAssertions;

using NetShield.Identity.Users;

namespace NetShield.UnitTests.Identity;

/// <summary>
/// Normalisation is what makes the unique index mean "one account per name" rather than "one
/// account per spelling of a name".
/// </summary>
public sealed class UserNameTests
{
    [Theory]
    [InlineData("admin", "admin")]
    [InlineData("Admin", "admin")]
    [InlineData("ADMIN", "admin")]
    [InlineData("  admin  ", "admin")]
    [InlineData("Net.Admin-01", "net.admin-01")]
    public void Normalize_ReducesEverySpellingToOne(string input, string expected)
    {
        UserName.Normalize(input).Should().Be(expected);
    }

    [Fact]
    public void Normalize_LowersInvariantly()
    {
        // The Turkish dotted capital I lowercases to a dotless i under tr-TR, which would make
        // ADMIN and admin two different accounts on a server nobody thought to check the locale of.
        UserName.Normalize("ADMIN").Should().Be("admin");
    }
}
