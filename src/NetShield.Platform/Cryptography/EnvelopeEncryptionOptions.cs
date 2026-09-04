namespace NetShield.Platform.Cryptography;

/// <summary>
/// The key-encryption keys NetShield wraps data-encryption keys with. Bound from
/// <c>Security:CredentialEncryption</c>.
/// </summary>
/// <remarks>
/// <para>
/// A value here is a 256-bit random key, encoded as base64 — not a pass phrase, and not
/// something a key is derived from. A key-encryption key has to be at least as strong as the
/// keys it protects, and no derivation adds entropy that the input did not have; so the
/// configuration contract is "this is the key", and a value that does not decode to exactly
/// <see cref="KeyEncryptionKeyRing.KeyLengthBytes"/> bytes fails the host at startup rather than
/// wrapping anything.
/// </para>
/// <para>
/// More than one key may be present. <see cref="ActiveKeyId"/> is what new material is wrapped
/// with; every other key stays so that material wrapped under it can still be opened, which is
/// what makes rotation a re-wrap in the background rather than an outage. A key may be dropped
/// once <c>NetShield.Web.Host --rewrap</c> reports nothing left on it.
/// </para>
/// <para>
/// Where the value comes from is a deployment question this package does not answer. In
/// development it is an Aspire parameter, generated once and persisted to the AppHost's user
/// secrets. In a deployment it must arrive from a secret store, a mounted file or a KMS — and
/// it must not live beside the database whose rows it opens.
/// </para>
/// </remarks>
public sealed class EnvelopeEncryptionOptions
{
    /// <summary>The configuration section this binds from.</summary>
    public const string SectionName = "Security:CredentialEncryption";

    /// <summary>
    /// Which key in <see cref="Keys"/> wraps new material. Required.
    /// </summary>
    public string? ActiveKeyId { get; set; }

    /// <summary>
    /// Every key the ring can unwrap with, keyed by the id stored alongside each ciphertext.
    /// Each value is base64 of exactly 32 bytes.
    /// </summary>
    public IDictionary<string, string> Keys { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
