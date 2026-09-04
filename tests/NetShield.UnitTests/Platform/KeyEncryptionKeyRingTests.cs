using System.Security.Cryptography;

using FluentAssertions;

using Microsoft.Extensions.Options;

using NetShield.Platform.Cryptography;

namespace NetShield.UnitTests.Platform;

/// <summary>
/// The configuration contract for a key-encryption key: exactly 32 random bytes, base64-encoded,
/// checked before the host starts rather than at the first request that needs a credential.
/// </summary>
public sealed class KeyEncryptionKeyRingTests
{
    private const string ValidKey = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";

    [Fact]
    public void Validate_WithAWellFormedRing_ReportsNothing() =>
        KeyEncryptionKeyRing.Validate(OptionsFor("one", ("one", ValidKey))).Should().BeEmpty();

    [Fact]
    public void Validate_WithNoKeys_SaysSo()
    {
        IReadOnlyList<string> problems = KeyEncryptionKeyRing.Validate(new EnvelopeEncryptionOptions
        {
            ActiveKeyId = "one"
        });

        problems.Should().ContainSingle(problem => problem.Contains("Keys is empty", StringComparison.Ordinal));
    }

    /// <summary>
    /// A short key is the failure that matters: it would work, and it would protect every stored
    /// credential with less than the 256 bits the data keys it wraps are worth.
    /// </summary>
    [Fact]
    public void Validate_WithAKeyOfTheWrongLength_Fails()
    {
        string sixteenBytes = Convert.ToBase64String(new byte[16]);

        KeyEncryptionKeyRing.Validate(OptionsFor("one", ("one", sixteenBytes)))
            .Should().ContainSingle(problem => problem.Contains("16 bytes", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("not base64 at all")]
    [InlineData("")]
    public void Validate_WithAValueThatIsNotBase64_Fails(string value) =>
        KeyEncryptionKeyRing.Validate(OptionsFor("one", ("one", value)))
            .Should().ContainSingle(problem => problem.Contains("Keys:one", StringComparison.Ordinal));

    [Fact]
    public void Validate_WithAKeyLongerThanTheRequiredLength_Fails() =>
        KeyEncryptionKeyRing.Validate(OptionsFor("one", ("one", Convert.ToBase64String(new byte[64]))))
            .Should().NotBeEmpty();

    [Fact]
    public void Validate_WithNoActiveKeyId_SaysWhatItIsFor() =>
        KeyEncryptionKeyRing.Validate(OptionsFor(activeKeyId: null, ("one", ValidKey)))
            .Should().ContainSingle(problem => problem.Contains("ActiveKeyId", StringComparison.Ordinal));

    [Fact]
    public void Validate_WhenTheActiveKeyIsNotInTheRing_Fails() =>
        KeyEncryptionKeyRing.Validate(OptionsFor("missing", ("one", ValidKey)))
            .Should().ContainSingle(problem => problem.Contains("'missing'", StringComparison.Ordinal));

    /// <summary>
    /// SPEC.md §5 applies to an error message as much as to a log line, and a key-encryption key
    /// is the credential that opens every other one.
    /// </summary>
    [Fact]
    public void Validate_NeverQuotesTheKeyMaterial()
    {
        IReadOnlyList<string> problems = KeyEncryptionKeyRing.Validate(
            OptionsFor("missing", ("one", ValidKey), ("two", Convert.ToBase64String(new byte[8]))));

        problems.Should().NotBeEmpty();
        problems.Should().AllSatisfy(problem => problem.Should().NotContain(ValidKey));
    }

    [Fact]
    public void Constructor_WithAWellFormedRing_ExposesEveryKeyId()
    {
        KeyEncryptionKeyRing ring = new(Options.Create(
            OptionsFor("two", ("one", ValidKey), ("two", ValidKey))));

        ring.ActiveKeyId.Should().Be("two");
        ring.KeyIds.Should().BeEquivalentTo(["one", "two"]);
    }

    /// <summary>
    /// Checked in the constructor as well as by the options validator: the ring is also built by
    /// the rotation command and by tests, and one that trusted somebody else to have checked is
    /// one that is eventually built unchecked.
    /// </summary>
    [Fact]
    public void Constructor_WithAMalformedRing_Throws()
    {
        IOptions<EnvelopeEncryptionOptions> options = Options.Create(OptionsFor("one", ("one", "nonsense")));

        FluentActions.Invoking(() => new KeyEncryptionKeyRing(options))
            .Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void Indexer_WithAnUnknownKeyId_Throws()
    {
        KeyEncryptionKeyRing ring = new(Options.Create(OptionsFor("one", ("one", ValidKey))));

        FluentActions.Invoking(() => ring["retired"])
            .Should().Throw<CryptographicException>()
            .WithMessage("*retired*");
    }

    [Fact]
    public void Validator_ReportsTheSameProblemsAsTheRing()
    {
        EnvelopeEncryptionOptions options = OptionsFor("one", ("one", "nonsense"));

        ValidateOptionsResult result = new EnvelopeEncryptionOptionsValidator().Validate(null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().BeEquivalentTo(KeyEncryptionKeyRing.Validate(options));
    }

    private static EnvelopeEncryptionOptions OptionsFor(string? activeKeyId, params (string Id, string Key)[] keys)
    {
        EnvelopeEncryptionOptions options = new() { ActiveKeyId = activeKeyId };

        foreach ((string id, string key) in keys)
        {
            options.Keys[id] = key;
        }

        return options;
    }
}
