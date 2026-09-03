using FluentAssertions;

using NetShield.Identity.Authentication;

namespace NetShield.UnitTests.Identity;

/// <summary>
/// A refresh token is the one credential that can mint a session without a password, so its
/// entropy and the fact that only its digest is stored are both load-bearing.
/// </summary>
public sealed class RefreshTokenGeneratorTests
{
    [Fact]
    public void Create_ProducesADistinctTokenEveryTime()
    {
        HashSet<string> tokens = [.. Enumerable.Range(0, 256).Select(_ => RefreshTokenGenerator.Create())];

        tokens.Should().HaveCount(256);
    }

    [Fact]
    public void Create_ProducesAUrlSafeTokenCarryingTheFullEntropy()
    {
        string token = RefreshTokenGenerator.Create();

        token.Should().MatchRegex("^[A-Za-z0-9_-]+$", "the token travels in a cookie and is never escaped");

        // 32 bytes in unpadded base64.
        token.Should().HaveLength(43);
    }

    [Fact]
    public void Hash_IsStableForTheSameToken()
    {
        string token = RefreshTokenGenerator.Create();

        RefreshTokenGenerator.Hash(token).Should().Be(RefreshTokenGenerator.Hash(token));
    }

    [Fact]
    public void Hash_DoesNotContainTheToken()
    {
        string token = RefreshTokenGenerator.Create();

        string hash = RefreshTokenGenerator.Hash(token);

        hash.Should().NotContain(token).And.HaveLength(64).And.MatchRegex("^[0-9a-f]+$");
    }

    [Fact]
    public void Hash_DiffersForDifferentTokens()
    {
        RefreshTokenGenerator.Hash(RefreshTokenGenerator.Create())
            .Should().NotBe(RefreshTokenGenerator.Hash(RefreshTokenGenerator.Create()));
    }
}
