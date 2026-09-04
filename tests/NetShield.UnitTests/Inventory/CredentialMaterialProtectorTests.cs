using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using FluentAssertions;

using NetShield.Contracts.Inventory;

using NetShield.Inventory.Credentials;
using NetShield.Inventory.Endpoints;

using NetShield.Platform.Cryptography;
using NetShield.Platform.Logging;

using NetShield.UnitTests.Platform;

namespace NetShield.UnitTests.Inventory;

/// <summary>
/// The two places a credential's plaintext exists, and the fact that neither of them is anywhere
/// the API can write a response from.
/// </summary>
public sealed class CredentialMaterialProtectorTests
{
    private static readonly Guid ProfileId = Guid.Parse("0199a0f0-0000-7000-8000-000000000001");

    [Fact]
    public void Seal_ThenOpen_ReturnsEveryMember()
    {
        CredentialMaterialProtector protector = ProtectorFor(out _);

        CredentialMaterialPayload material = new()
        {
            PrivateKey = "-----BEGIN PRIVATE KEY-----\nabc\n-----END PRIVATE KEY-----\n",
            PrivateKeyPassword = "protected"
        };

        EnvelopeCiphertext ciphertext = protector.Seal(ProfileId, material);

        protector.Open(ProfileFor(ProfileId, ciphertext)).Should().Be(material);
    }

    /// <summary>
    /// The ciphertext is bound to the profile it belongs to. A blob moved into another row's
    /// columns does not open, rather than handing that row's would-be credential to whoever
    /// moved it.
    /// </summary>
    [Fact]
    public void Open_AgainstADifferentProfileId_Throws()
    {
        CredentialMaterialProtector protector = ProtectorFor(out _);

        EnvelopeCiphertext ciphertext = protector.Seal(
            ProfileId,
            new CredentialMaterialPayload { Community = "read-only" });

        CredentialProfile elsewhere = ProfileFor(Guid.CreateVersion7(), ciphertext);

        protector.Invoking(subject => subject.Open(elsewhere)).Should().Throw<CryptographicException>();
    }

    /// <summary>
    /// SPEC.md §5 in its most literal form: the bytes that reach the column contain none of the
    /// secret they were made from.
    /// </summary>
    [Fact]
    public void Seal_ProducesBytesThatDoNotContainThePlaintext()
    {
        const string Community = "a-community-nobody-should-see";

        EnvelopeCiphertext ciphertext = ProtectorFor(out _)
            .Seal(ProfileId, new CredentialMaterialPayload { Community = Community });

        Encoding.UTF8.GetString(ciphertext.Payload).Should().NotContain(Community);
        Convert.ToBase64String(ciphertext.Payload).Should()
            .NotContain(Convert.ToBase64String(Encoding.UTF8.GetBytes(Community)));
    }

    /// <summary>
    /// The shape at rest is its own type on purpose: the wire contract may be renamed with the
    /// API, and bytes already in a column have to keep opening. This pins the stored member names.
    /// </summary>
    [Fact]
    public void ThePayloadAtRest_UsesTheJsonNamesItWasWrittenWith()
    {
        string json = JsonSerializer.Serialize(
            new CredentialMaterialPayload
            {
                Community = "c",
                AuthPassword = "a",
                PrivacyPassword = "p",
                Password = "s",
                PrivateKey = "k",
                PrivateKeyPassword = "kp"
            },
            CredentialMaterialSerializerContext.Default.CredentialMaterialPayload);

        json.Should().Contain("\"community\"")
            .And.Contain("\"authPassword\"")
            .And.Contain("\"privacyPassword\"")
            .And.Contain("\"password\"")
            .And.Contain("\"privateKey\"")
            .And.Contain("\"privateKeyPassword\"");
    }

    /// <summary>
    /// The serialiser the API writes responses with cannot write the plaintext shape at all. It
    /// is not a matter of no endpoint returning one — the type is not in the context, so there is
    /// no <c>JsonTypeInfo</c> to serialise it through.
    /// </summary>
    [Fact]
    public void TheApiSerializer_CannotWriteThePlaintextShape() =>
        InventorySerializerContext.Default.GetTypeInfo(typeof(CredentialMaterialPayload))
            .Should().BeNull(
                "the shape credentials are stored as must have no path to a response body");

    /// <summary>
    /// The contract type is in the API serialiser, because two requests carry it. That is the
    /// "write-only" half: in, never out.
    /// </summary>
    [Fact]
    public void TheApiSerializer_CanReadTheRequestShape() =>
        InventorySerializerContext.Default.GetTypeInfo(typeof(CredentialMaterial))
            .Should().NotBeNull();

    /// <summary>
    /// Nothing on a response shape may be named something the redactor would blank — the
    /// reflection counterpart of the OpenAPI walk in <c>ApiSecretExposureTests</c>.
    /// </summary>
    [Theory]
    [InlineData(typeof(CredentialProfileDetail))]
    [InlineData(typeof(CredentialProfileSummary))]
    public void NoResponseShape_HasAMemberNamedLikeASecret(Type shape)
    {
        SecretRedactor redactor = new();

        JsonTypeInfo info = InventorySerializerContext.Default.GetTypeInfo(shape)!;

        info.Properties.Select(property => property.Name)
            .Where(redactor.IsSecretName)
            .Should().BeEmpty();
    }

    private static CredentialMaterialProtector ProtectorFor(out IEnvelopeEncryptor encryptor)
    {
        encryptor = EnvelopeEncryptionTests.EncryptorFor(
            "test",
            ("test", "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8="));

        return new CredentialMaterialProtector(encryptor);
    }

    private static CredentialProfile ProfileFor(Guid id, EnvelopeCiphertext ciphertext) => new()
    {
        Id = id,
        Name = "Core",
        NormalizedName = "core",
        Kind = CredentialKind.SnmpV2c,
        KeyId = ciphertext.KeyId,
        WrappedDataKey = ciphertext.WrappedDataKey,
        MaterialCiphertext = ciphertext.Payload,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        MaterialUpdatedAt = DateTimeOffset.UtcNow
    };
}
