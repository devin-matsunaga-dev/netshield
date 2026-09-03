using FluentAssertions;

using NetShield.Identity.Passwords;

namespace NetShield.UnitTests.Identity;

/// <summary>
/// CONVENTIONS.md §7 names credential handling as an area that needs tests before the package
/// closes. This is the whole of what the login path trusts.
/// </summary>
public sealed class Argon2idPasswordHasherTests
{
    /// <summary>The smallest work factor the options allow, so the suite stays quick.</summary>
    private static PasswordHashingOptions FastOptions => new()
    {
        MemoryKib = 8 * 1024,
        Iterations = 1,
        Parallelism = 1
    };

    private static Argon2idPasswordHasher Hasher(PasswordHashingOptions? options = null) =>
        new(TestOptions.For(options ?? FastOptions));

    [Fact]
    public async Task VerifyAsync_WithTheSamePassword_Matches()
    {
        Argon2idPasswordHasher hasher = Hasher();

        string stored = await hasher.HashAsync("correct horse battery staple", TestContext.Current.CancellationToken);

        PasswordVerification result = await hasher.VerifyAsync(
            "correct horse battery staple",
            stored,
            TestContext.Current.CancellationToken);

        result.IsMatch.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyAsync_WithADifferentPassword_DoesNotMatch()
    {
        Argon2idPasswordHasher hasher = Hasher();

        string stored = await hasher.HashAsync("correct horse battery staple", TestContext.Current.CancellationToken);

        PasswordVerification result = await hasher.VerifyAsync(
            "correct horse battery stapl",
            stored,
            TestContext.Current.CancellationToken);

        result.IsMatch.Should().BeFalse();
    }

    [Fact]
    public async Task HashAsync_ForTheSamePasswordTwice_ProducesDifferentHashes()
    {
        Argon2idPasswordHasher hasher = Hasher();

        string first = await hasher.HashAsync("Sup3rSecret!Value", TestContext.Current.CancellationToken);
        string second = await hasher.HashAsync("Sup3rSecret!Value", TestContext.Current.CancellationToken);

        first.Should().NotBe(second, "each hash carries its own salt, so identical passwords are not identical rows");
    }

    [Fact]
    public async Task HashAsync_WritesThePhcStringWithTheConfiguredCosts()
    {
        PasswordHashingOptions options = new() { MemoryKib = 8 * 1024, Iterations = 3, Parallelism = 2 };

        string stored = await Hasher(options).HashAsync("Sup3rSecret!Value", TestContext.Current.CancellationToken);

        stored.Should().StartWith("$argon2id$v=19$m=8192,t=3,p=2$");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("$argon2id$v=19$m=8192,t=1,p=1$corrupt")]
    public async Task VerifyAsync_WithAnUnusableStoredHash_DoesNotMatch(string? stored)
    {
        PasswordVerification result = await Hasher().VerifyAsync(
            "Sup3rSecret!Value",
            stored,
            TestContext.Current.CancellationToken);

        result.Should().Be(PasswordVerification.Failed);
    }

    [Fact]
    public async Task VerifyAsync_WhenTheStoredHashIsWeakerThanConfigured_AsksForARehash()
    {
        PasswordHashingOptions weak = new() { MemoryKib = 8 * 1024, Iterations = 1, Parallelism = 1 };
        PasswordHashingOptions strong = new() { MemoryKib = 16 * 1024, Iterations = 2, Parallelism = 1 };

        string stored = await Hasher(weak).HashAsync("Sup3rSecret!Value", TestContext.Current.CancellationToken);

        PasswordVerification result = await Hasher(strong).VerifyAsync(
            "Sup3rSecret!Value",
            stored,
            TestContext.Current.CancellationToken);

        result.Should().Be(new PasswordVerification(IsMatch: true, NeedsRehash: true));
    }

    [Fact]
    public async Task VerifyAsync_WhenTheStoredHashMatchesTheConfiguredCosts_DoesNotAskForARehash()
    {
        Argon2idPasswordHasher hasher = Hasher();

        string stored = await hasher.HashAsync("Sup3rSecret!Value", TestContext.Current.CancellationToken);

        PasswordVerification result = await hasher.VerifyAsync(
            "Sup3rSecret!Value",
            stored,
            TestContext.Current.CancellationToken);

        result.Should().Be(new PasswordVerification(IsMatch: true, NeedsRehash: false));
    }

    [Fact]
    public async Task VerifyAsync_WithATamperedDigest_DoesNotMatch()
    {
        Argon2idPasswordHasher hasher = Hasher();

        string stored = await hasher.HashAsync("Sup3rSecret!Value", TestContext.Current.CancellationToken);

        // Flip the last character of the digest field, leaving a structurally valid PHC string.
        char last = stored[^1];
        string tampered = stored[..^1] + (last == 'A' ? 'B' : 'A');

        PasswordVerification result = await hasher.VerifyAsync(
            "Sup3rSecret!Value",
            tampered,
            TestContext.Current.CancellationToken);

        result.IsMatch.Should().BeFalse();
    }
}
