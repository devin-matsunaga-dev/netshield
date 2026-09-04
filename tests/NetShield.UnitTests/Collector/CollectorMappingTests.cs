using FluentAssertions;

using NetShield.Contracts.Inventory;

using NetShield.Inventory.Collector;
using NetShield.Inventory.Collector.Contract;
using NetShield.Inventory.Credentials;

namespace NetShield.UnitTests.Collector;

/// <summary>
/// The one place a stored credential becomes the shape the collector receives.
/// </summary>
/// <remarks>
/// Written out member by member on purpose, so this test can be what notices when a member is
/// added to the payload and not to the wire — a reflective copy would carry it silently, and
/// whether the collector should receive a new secret is a decision rather than a consequence.
/// </remarks>
public sealed class CollectorMappingTests
{
    [Fact]
    public void EverySecretMember_ReachesTheWireShape()
    {
        CollectorJobCredential mapped = CollectorMapping.ToCredential(new ResolvedCredential(
            Guid.CreateVersion7(),
            CredentialKind.SnmpV3,
            "netshield-ro",
            SnmpAuthAlgorithm.Sha256,
            SnmpPrivacyAlgorithm.Aes128,
            new CredentialMaterialPayload
            {
                Community = "community",
                AuthPassword = "auth",
                PrivacyPassword = "privacy",
                Password = "password",
                PrivateKey = "key",
                PrivateKeyPassword = "key-password"
            }));

        mapped.Material.Community.Should().Be("community");
        mapped.Material.AuthPassword.Should().Be("auth");
        mapped.Material.PrivacyPassword.Should().Be("privacy");
        mapped.Material.Password.Should().Be("password");
        mapped.Material.PrivateKey.Should().Be("key");
        mapped.Material.PrivateKeyPassword.Should().Be("key-password");
    }

    [Fact]
    public void TheWireShape_HasNoMemberThePayloadDoesNot()
    {
        // The other direction of the same rule: a member added to the collector's contract that
        // nothing at rest can fill would be a member the API invents.
        IReadOnlyList<string> wire = [.. typeof(CollectorCredentialMaterial)
            .GetProperties()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)];

        IReadOnlyList<string> payload = [.. typeof(CredentialMaterialPayload)
            .GetProperties()
            .Select(property => property.Name)
            .Where(name => name != "EqualityContract")
            .Order(StringComparer.Ordinal)];

        wire.Where(name => name != "EqualityContract").Should().Equal(payload);
    }

    [Fact]
    public void TheDescribingMembers_TravelAlongsideTheMaterial()
    {
        Guid profileId = Guid.CreateVersion7();

        CollectorJobCredential mapped = CollectorMapping.ToCredential(new ResolvedCredential(
            profileId,
            CredentialKind.SshKey,
            "netshield-ro",
            AuthAlgorithm: null,
            PrivacyAlgorithm: null,
            new CredentialMaterialPayload { PrivateKey = "key" }));

        mapped.CredentialProfileId.Should().Be(profileId);
        mapped.Kind.Should().Be(CredentialKind.SshKey);
        mapped.Username.Should().Be("netshield-ro");
        mapped.AuthAlgorithm.Should().BeNull();
        mapped.PrivacyAlgorithm.Should().BeNull();
    }
}
