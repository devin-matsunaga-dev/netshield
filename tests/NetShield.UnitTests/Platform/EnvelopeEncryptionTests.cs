using System.Security.Cryptography;
using System.Text;

using FluentAssertions;

using Microsoft.Extensions.Options;

using NetShield.Platform.Cryptography;

namespace NetShield.UnitTests.Platform;

/// <summary>
/// The guarantees WP-1.2 rests on: a sealed value opens only with the right key and the right
/// context, an altered one does not open at all, and a re-wrap moves the key without ever
/// reconstructing the plaintext.
/// </summary>
public sealed class EnvelopeEncryptionTests
{
    private const string Plaintext = "public-community-string-stand-in";

    private const string Context = "credential-profile:0199a0f0-0000-7000-8000-000000000001";

    /// <summary>Bytes 0x00 to 0x1f. A fixture, not a key anybody generated.</summary>
    private const string KeyOne = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";

    /// <summary>Bytes 0x20 to 0x3f.</summary>
    private const string KeyTwo = "ICEiIyQlJicoKSorLC0uLzAxMjM0NTY3ODk6Ozw9Pj8=";

    [Fact]
    public void Encrypt_ThenDecrypt_ReturnsThePlaintext()
    {
        IEnvelopeEncryptor encryptor = EncryptorFor("one", ("one", KeyOne));

        EnvelopeCiphertext envelope = encryptor.Encrypt(Encoding.UTF8.GetBytes(Plaintext), Context);

        Encoding.UTF8.GetString(encryptor.Decrypt(envelope, Context)).Should().Be(Plaintext);
    }

    [Fact]
    public void Encrypt_NamesTheActiveKey()
    {
        IEnvelopeEncryptor encryptor = EncryptorFor("two", ("one", KeyOne), ("two", KeyTwo));

        encryptor.Encrypt(Encoding.UTF8.GetBytes(Plaintext), Context).KeyId.Should().Be("two");
    }

    /// <summary>
    /// A fresh data key and a fresh nonce for every seal, so two profiles holding the same
    /// password do not hold the same bytes — which would let anyone with the table tell that they
    /// match without opening either.
    /// </summary>
    [Fact]
    public void Encrypt_TwiceOverTheSamePlaintext_ProducesDifferentCiphertext()
    {
        IEnvelopeEncryptor encryptor = EncryptorFor("one", ("one", KeyOne));

        EnvelopeCiphertext first = encryptor.Encrypt(Encoding.UTF8.GetBytes(Plaintext), Context);
        EnvelopeCiphertext second = encryptor.Encrypt(Encoding.UTF8.GetBytes(Plaintext), Context);

        second.Payload.Should().NotEqual(first.Payload);
        second.WrappedDataKey.Should().NotEqual(first.WrappedDataKey);
    }

    /// <summary>
    /// The context is additional authenticated data. A blob copied into another profile's columns
    /// fails rather than yielding that profile's would-be credential to whoever moved it.
    /// </summary>
    [Fact]
    public void Decrypt_UnderADifferentContext_Throws()
    {
        IEnvelopeEncryptor encryptor = EncryptorFor("one", ("one", KeyOne));

        EnvelopeCiphertext envelope = encryptor.Encrypt(Encoding.UTF8.GetBytes(Plaintext), Context);

        encryptor.Invoking(subject => subject.Decrypt(envelope, "credential-profile:someone-else"))
            .Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Decrypt_WithAnAlteredPayload_Throws()
    {
        IEnvelopeEncryptor encryptor = EncryptorFor("one", ("one", KeyOne));

        EnvelopeCiphertext envelope = encryptor.Encrypt(Encoding.UTF8.GetBytes(Plaintext), Context);

        envelope.Payload[^1] ^= 0xff;

        encryptor.Invoking(subject => subject.Decrypt(envelope, Context))
            .Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Decrypt_WithAnAlteredWrappedKey_Throws()
    {
        IEnvelopeEncryptor encryptor = EncryptorFor("one", ("one", KeyOne));

        EnvelopeCiphertext envelope = encryptor.Encrypt(Encoding.UTF8.GetBytes(Plaintext), Context);

        envelope.WrappedDataKey[0] ^= 0xff;

        encryptor.Invoking(subject => subject.Decrypt(envelope, Context))
            .Should().Throw<CryptographicException>();
    }

    /// <summary>
    /// The whole of SPEC.md §5's "unreadable without the KEK": the stored row is exactly what a
    /// second process holding a different ring cannot open.
    /// </summary>
    [Fact]
    public void Decrypt_UnderADifferentKeyOfTheSameId_Throws()
    {
        EnvelopeCiphertext envelope = EncryptorFor("one", ("one", KeyOne))
            .Encrypt(Encoding.UTF8.GetBytes(Plaintext), Context);

        IEnvelopeEncryptor other = EncryptorFor("one", ("one", KeyTwo));

        other.Invoking(subject => subject.Decrypt(envelope, Context))
            .Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Decrypt_WhenTheKeyIsNotInTheRing_ThrowsNamingTheKeyIdAndNoKeyMaterial()
    {
        EnvelopeCiphertext envelope = EncryptorFor("one", ("one", KeyOne))
            .Encrypt(Encoding.UTF8.GetBytes(Plaintext), Context);

        IEnvelopeEncryptor withoutIt = EncryptorFor("two", ("two", KeyTwo));

        string message = withoutIt.Invoking(subject => subject.Decrypt(envelope, Context))
            .Should().Throw<CryptographicException>().Which.Message;

        message.Should().Contain("one");
        message.Should().NotContain(KeyOne).And.NotContain(KeyTwo);
    }

    [Fact]
    public void TryRewrap_WhenAlreadyOnTheActiveKey_ReturnsFalseAndChangesNothing()
    {
        IEnvelopeEncryptor encryptor = EncryptorFor("one", ("one", KeyOne));

        EnvelopeCiphertext envelope = encryptor.Encrypt(Encoding.UTF8.GetBytes(Plaintext), Context);

        encryptor.TryRewrap(envelope, Context, out EnvelopeCiphertext moved).Should().BeFalse();

        moved.Should().BeSameAs(envelope);
    }

    /// <summary>
    /// The property that makes rotation cheap and safe: only the wrapped key is rewritten, so the
    /// plaintext is never reconstructed and the payload column is never touched.
    /// </summary>
    [Fact]
    public void TryRewrap_MovesTheKeyIdAndLeavesThePayloadByteForByte()
    {
        EnvelopeCiphertext envelope = EncryptorFor("one", ("one", KeyOne))
            .Encrypt(Encoding.UTF8.GetBytes(Plaintext), Context);

        IEnvelopeEncryptor rotating = EncryptorFor("two", ("one", KeyOne), ("two", KeyTwo));

        rotating.TryRewrap(envelope, Context, out EnvelopeCiphertext moved).Should().BeTrue();

        moved.KeyId.Should().Be("two");
        moved.Payload.Should().Equal(envelope.Payload);
        moved.WrappedDataKey.Should().NotEqual(envelope.WrappedDataKey);
    }

    /// <summary>
    /// After a rotation the old key can be retired: a ring holding only the new one still opens
    /// everything that was moved.
    /// </summary>
    [Fact]
    public void Decrypt_AfterRewrap_SucceedsWithTheOldKeyRemovedFromTheRing()
    {
        EnvelopeCiphertext envelope = EncryptorFor("one", ("one", KeyOne))
            .Encrypt(Encoding.UTF8.GetBytes(Plaintext), Context);

        EncryptorFor("two", ("one", KeyOne), ("two", KeyTwo))
            .TryRewrap(envelope, Context, out EnvelopeCiphertext moved);

        IEnvelopeEncryptor afterRetirement = EncryptorFor("two", ("two", KeyTwo));

        Encoding.UTF8.GetString(afterRetirement.Decrypt(moved, Context)).Should().Be(Plaintext);
    }

    /// <summary>A sealed value shorter than a nonce and a tag is not one of ours.</summary>
    [Fact]
    public void Decrypt_OfSomethingTooShortToBeSealed_Throws()
    {
        IEnvelopeEncryptor encryptor = EncryptorFor("one", ("one", KeyOne));

        EnvelopeCiphertext malformed = new("one", new byte[8], new byte[8]);

        encryptor.Invoking(subject => subject.Decrypt(malformed, Context))
            .Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Encrypt_WithNoContext_Throws()
    {
        IEnvelopeEncryptor encryptor = EncryptorFor("one", ("one", KeyOne));

        encryptor.Invoking(subject => subject.Encrypt(Encoding.UTF8.GetBytes(Plaintext), string.Empty))
            .Should().Throw<ArgumentException>();
    }

    internal static AesGcmEnvelopeEncryptor EncryptorFor(
        string activeKeyId,
        params (string Id, string Key)[] keys) =>
        new(RingFor(activeKeyId, keys));

    internal static KeyEncryptionKeyRing RingFor(string activeKeyId, params (string Id, string Key)[] keys)
    {
        EnvelopeEncryptionOptions options = new() { ActiveKeyId = activeKeyId };

        foreach ((string id, string key) in keys)
        {
            options.Keys[id] = key;
        }

        return new KeyEncryptionKeyRing(Options.Create(options));
    }
}
