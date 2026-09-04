using System.Security.Cryptography;
using System.Text.Json;

using NetShield.Platform.Cryptography;

namespace NetShield.Inventory.Credentials;

/// <summary>
/// The only two places a credential's plaintext exists: on its way into the sealed blob, and on
/// its way back out.
/// </summary>
/// <remarks>
/// <para>
/// It is a type of its own so that both directions are written once. A handler that serialised
/// and sealed inline would be a handler somebody copies, and the copy is where the context
/// argument gets passed wrong or the intermediate buffer stops being cleared.
/// </para>
/// <para>
/// The JSON bytes are zeroed after use in both directions. That is not a guarantee — a string
/// the serialiser interned along the way is not ours to erase, and .NET gives no way to pin one
/// — but the largest and longest-lived buffer is the one that is cleared, and leaving it full of
/// a private key would be a choice rather than a limitation.
/// </para>
/// </remarks>
internal sealed class CredentialMaterialProtector(IEnvelopeEncryptor encryptor)
{
    /// <summary>Seals <paramref name="material"/> for the profile with this id.</summary>
    public EnvelopeCiphertext Seal(Guid profileId, CredentialMaterialPayload material)
    {
        ArgumentNullException.ThrowIfNull(material);

        byte[] json = JsonSerializer.SerializeToUtf8Bytes(
            material,
            CredentialMaterialSerializerContext.Default.CredentialMaterialPayload);

        try
        {
            return encryptor.Encrypt(json, CredentialProfile.ContextFor(profileId));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(json);
        }
    }

    /// <summary>Opens a profile's sealed material.</summary>
    /// <exception cref="CryptographicException">
    /// The key that sealed it is not configured, or the row has been altered. Either is an
    /// infrastructure fault rather than something a caller asked for wrongly.
    /// </exception>
    public CredentialMaterialPayload Open(CredentialProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        byte[] json = encryptor.Decrypt(profile.Ciphertext, CredentialProfile.ContextFor(profile.Id));

        try
        {
            return JsonSerializer.Deserialize(
                    json,
                    CredentialMaterialSerializerContext.Default.CredentialMaterialPayload)
                ?? throw new CryptographicException("The sealed material opened to nothing.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(json);
        }
    }
}
