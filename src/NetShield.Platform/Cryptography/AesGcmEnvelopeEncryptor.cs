using System.Security.Cryptography;
using System.Text;

namespace NetShield.Platform.Cryptography;

/// <summary>
/// Envelope encryption with AES-256-GCM at both layers.
/// </summary>
/// <remarks>
/// <para>
/// A fresh 256-bit data-encryption key is generated for every value, the value is sealed under
/// it, and the key is sealed under the ring's active key-encryption key. Nothing but the
/// key-encryption key is long-lived, and it never reaches the database — so the stored row is
/// unreadable to anyone holding a copy of the database and not the key (SPEC.md §5).
/// </para>
/// <para>
/// GCM rather than CBC-plus-HMAC because it authenticates as it encrypts: a row that has been
/// altered fails to open rather than yielding plausible bytes. The <c>context</c> is bound in as
/// additional authenticated data at both layers, so a ciphertext is tied to the row it belongs
/// to and moving one between rows breaks it.
/// </para>
/// <para>
/// Both outputs are laid out as <c>nonce (12) || ciphertext || tag (16)</c>. A nonce is drawn
/// fresh from <see cref="RandomNumberGenerator"/> for every single seal — GCM's one
/// unforgiving requirement is that a nonce is never reused under the same key, and since every
/// value has its own data-encryption key the payload nonce is used exactly once by construction.
/// </para>
/// </remarks>
public sealed class AesGcmEnvelopeEncryptor(KeyEncryptionKeyRing ring) : IEnvelopeEncryptor
{
    /// <summary>GCM's standard nonce length, and the only one it is specified for.</summary>
    private const int NonceBytes = 12;

    /// <summary>The full-length authentication tag. Anything shorter weakens the guarantee.</summary>
    private const int TagBytes = 16;

    /// <summary>AES-256, matching the key-encryption key length.</summary>
    private const int DataKeyBytes = 32;

    public EnvelopeCiphertext Encrypt(ReadOnlySpan<byte> plaintext, string context)
    {
        ArgumentException.ThrowIfNullOrEmpty(context);

        byte[] associatedData = Encoding.UTF8.GetBytes(context);
        byte[] dataKey = RandomNumberGenerator.GetBytes(DataKeyBytes);

        try
        {
            return new EnvelopeCiphertext(
                ring.ActiveKeyId,
                Seal(ring[ring.ActiveKeyId], dataKey, associatedData),
                Seal(dataKey, plaintext, associatedData));
        }
        finally
        {
            // The wrapped copy is what persists. This one has no further use, and a key sitting
            // in a pooled array is a key that can be read out of a heap dump.
            CryptographicOperations.ZeroMemory(dataKey);
        }
    }

    public byte[] Decrypt(EnvelopeCiphertext ciphertext, string context)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);
        ArgumentException.ThrowIfNullOrEmpty(context);

        byte[] associatedData = Encoding.UTF8.GetBytes(context);
        byte[] dataKey = Open(ring[ciphertext.KeyId], ciphertext.WrappedDataKey, associatedData);

        try
        {
            return Open(dataKey, ciphertext.Payload, associatedData);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
        }
    }

    public bool TryRewrap(EnvelopeCiphertext ciphertext, string context, out EnvelopeCiphertext rewrapped)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);
        ArgumentException.ThrowIfNullOrEmpty(context);

        if (string.Equals(ciphertext.KeyId, ring.ActiveKeyId, StringComparison.Ordinal))
        {
            rewrapped = ciphertext;

            return false;
        }

        byte[] associatedData = Encoding.UTF8.GetBytes(context);
        byte[] dataKey = Open(ring[ciphertext.KeyId], ciphertext.WrappedDataKey, associatedData);

        try
        {
            // Only the wrapped key is rewritten. The payload is carried across untouched, so the
            // plaintext is never reconstructed and a rotation over a whole table is a sequence of
            // small updates rather than a decrypt-and-re-encrypt of everything.
            rewrapped = ciphertext with
            {
                KeyId = ring.ActiveKeyId,
                WrappedDataKey = Seal(ring[ring.ActiveKeyId], dataKey, associatedData)
            };

            return true;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
        }
    }

    /// <summary>Seals <paramref name="plaintext"/> into <c>nonce || ciphertext || tag</c>.</summary>
    private static byte[] Seal(byte[] key, ReadOnlySpan<byte> plaintext, byte[] associatedData)
    {
        byte[] envelope = new byte[NonceBytes + plaintext.Length + TagBytes];

        Span<byte> nonce = envelope.AsSpan(0, NonceBytes);
        Span<byte> ciphertext = envelope.AsSpan(NonceBytes, plaintext.Length);
        Span<byte> tag = envelope.AsSpan(NonceBytes + plaintext.Length, TagBytes);

        RandomNumberGenerator.Fill(nonce);

        using AesGcm aes = new(key, TagBytes);

        aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);

        return envelope;
    }

    /// <summary>Opens what <see cref="Seal"/> produced.</summary>
    /// <exception cref="CryptographicException">
    /// The value is too short to be one of ours, or the tag does not verify — a wrong key, a
    /// wrong context, or an altered row. The three are deliberately not told apart: which one it
    /// was is an oracle, and an operator reads the answer from the logs around it.
    /// </exception>
    private static byte[] Open(byte[] key, byte[] envelope, byte[] associatedData)
    {
        if (envelope.Length < NonceBytes + TagBytes)
        {
            throw new CryptographicException("The stored value is not a sealed value.");
        }

        byte[] plaintext = new byte[envelope.Length - NonceBytes - TagBytes];

        using AesGcm aes = new(key, TagBytes);

        aes.Decrypt(
            envelope.AsSpan(0, NonceBytes),
            envelope.AsSpan(NonceBytes, plaintext.Length),
            envelope.AsSpan(NonceBytes + plaintext.Length, TagBytes),
            plaintext,
            associatedData);

        return plaintext;
    }
}
