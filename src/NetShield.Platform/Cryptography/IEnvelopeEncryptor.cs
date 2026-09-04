namespace NetShield.Platform.Cryptography;

/// <summary>
/// Envelope encryption for data at rest (ARCHITECTURE.md §8): a fresh data-encryption key per
/// value, wrapped by a key-encryption key that comes from configuration and never from the
/// database.
/// </summary>
/// <remarks>
/// <para>
/// Every operation takes a <c>context</c>. It is bound into both ciphertexts as additional
/// authenticated data, so a sealed value only opens against the thing it was sealed for — a blob
/// copied from one row to another fails to decrypt rather than yielding the wrong device's
/// credential to whoever moved it.
/// </para>
/// <para>
/// Failures throw. A value that will not open means the wrong key ring is configured or the row
/// has been tampered with, and neither is something a caller can be handed a
/// <c>Result</c> about and carry on from (CONVENTIONS.md §2).
/// </para>
/// </remarks>
public interface IEnvelopeEncryptor
{
    /// <summary>Seals <paramref name="plaintext"/> under a new data-encryption key.</summary>
    /// <param name="plaintext">The value to seal.</param>
    /// <param name="context">
    /// What this value belongs to, bound into the ciphertext. Stable for the life of the value —
    /// a context that changes makes the value unreadable.
    /// </param>
    EnvelopeCiphertext Encrypt(ReadOnlySpan<byte> plaintext, string context);

    /// <summary>Opens <paramref name="ciphertext"/>.</summary>
    /// <exception cref="System.Security.Cryptography.CryptographicException">
    /// The key that wrapped it is not in the ring, the context does not match, or the value has
    /// been altered.
    /// </exception>
    byte[] Decrypt(EnvelopeCiphertext ciphertext, string context);

    /// <summary>
    /// Re-wraps <paramref name="ciphertext"/>'s data-encryption key under the active
    /// key-encryption key, leaving the payload untouched.
    /// </summary>
    /// <remarks>
    /// The payload is never re-encrypted and the plaintext is never reconstructed, which is what
    /// makes a rotation cheap enough to run over a whole table without taking anything offline.
    /// </remarks>
    /// <returns>
    /// <see langword="false"/> when the value is already on the active key, so a caller can skip
    /// the write.
    /// </returns>
    bool TryRewrap(EnvelopeCiphertext ciphertext, string context, out EnvelopeCiphertext rewrapped);
}
