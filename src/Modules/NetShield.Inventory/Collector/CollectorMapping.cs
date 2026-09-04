using NetShield.Inventory.Collector.Contract;
using NetShield.Inventory.Credentials;

namespace NetShield.Inventory.Collector;

/// <summary>
/// Turns what the module holds into what the collector is told.
/// </summary>
/// <remarks>
/// The credential mapping is written out member by member rather than by anything that copies
/// what it finds. A reflective or serialiser-based copy would carry a member added to the
/// payload later without anyone deciding that the collector should receive it, and the whole
/// point of three separate credential shapes is that each addition is a decision.
/// </remarks>
internal static class CollectorMapping
{
    /// <summary>The opened credential, as the collector receives it.</summary>
    internal static CollectorJobCredential ToCredential(ResolvedCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);

        return new CollectorJobCredential(
            credential.CredentialProfileId,
            credential.Kind,
            credential.Username,
            credential.AuthAlgorithm,
            credential.PrivacyAlgorithm,
            new CollectorCredentialMaterial
            {
                Community = credential.Material.Community,
                AuthPassword = credential.Material.AuthPassword,
                PrivacyPassword = credential.Material.PrivacyPassword,
                Password = credential.Material.Password,
                PrivateKey = credential.Material.PrivateKey,
                PrivateKeyPassword = credential.Material.PrivateKeyPassword
            });
    }
}
