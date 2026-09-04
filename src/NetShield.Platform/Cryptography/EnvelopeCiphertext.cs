namespace NetShield.Platform.Cryptography;

/// <summary>
/// One sealed value: the payload, the data-encryption key that opens it wrapped by a
/// key-encryption key, and the id of the key that wrapped it.
/// </summary>
/// <remarks>
/// <para>
/// The key id travels with the ciphertext because it is what makes rotation possible: a row says
/// which key opens it, so a ring holding the previous key and the current one can read
/// everything while a re-wrap works through the table.
/// </para>
/// <para>
/// Both byte arrays are <c>nonce || ciphertext || tag</c> — see
/// <see cref="AesGcmEnvelopeEncryptor"/>. They are opaque to every caller; nothing outside that
/// type may take them apart.
/// </para>
/// </remarks>
/// <param name="KeyId">The key-encryption key that <paramref name="WrappedDataKey"/> is wrapped by.</param>
/// <param name="WrappedDataKey">The data-encryption key, sealed under the key-encryption key.</param>
/// <param name="Payload">The value, sealed under the data-encryption key.</param>
public sealed record EnvelopeCiphertext(string KeyId, byte[] WrappedDataKey, byte[] Payload);
