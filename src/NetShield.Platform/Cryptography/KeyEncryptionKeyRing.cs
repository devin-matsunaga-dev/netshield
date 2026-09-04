using System.Collections.Frozen;
using System.Security.Cryptography;

using Microsoft.Extensions.Options;

namespace NetShield.Platform.Cryptography;

/// <summary>
/// The configured key-encryption keys, decoded once and held for the life of the process.
/// </summary>
/// <remarks>
/// <para>
/// Decoding happens here rather than at each use so that a malformed key is a startup failure
/// with a sentence an operator can act on, instead of a <c>CryptographicException</c> from the
/// first request that happened to need a credential.
/// </para>
/// <para>
/// No message this type produces contains key material, a fragment of it, or its decoded length
/// beyond what the operator already supplied — SPEC.md §5 admits no credential in an error
/// message, and a key is the credential that opens all the others.
/// </para>
/// </remarks>
public sealed class KeyEncryptionKeyRing
{
    /// <summary>How long every key-encryption key is. AES-256, so 32 bytes.</summary>
    public const int KeyLengthBytes = 32;

    private readonly FrozenDictionary<string, byte[]> keys;

    public KeyEncryptionKeyRing(IOptions<EnvelopeEncryptionOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        EnvelopeEncryptionOptions value = options.Value;

        // Validated again here rather than trusted from the options validator alone: this type is
        // also constructed directly by tests and by the rewrap command, and a key ring that
        // depends on somebody else having checked is a key ring that is one day built unchecked.
        IReadOnlyList<string> problems = Validate(value);

        if (problems.Count > 0)
        {
            throw new OptionsValidationException(
                EnvelopeEncryptionOptions.SectionName,
                typeof(EnvelopeEncryptionOptions),
                problems);
        }

        ActiveKeyId = value.ActiveKeyId!;

        keys = value.Keys.ToFrozenDictionary(
            entry => entry.Key,
            entry => Convert.FromBase64String(entry.Value),
            StringComparer.Ordinal);
    }

    /// <summary>The key new material is wrapped under.</summary>
    public string ActiveKeyId { get; }

    /// <summary>Every key id the ring can unwrap with.</summary>
    public IReadOnlyCollection<string> KeyIds => keys.Keys;

    /// <summary>The key with this id.</summary>
    /// <exception cref="CryptographicException">
    /// No key of that id is configured — which means material sealed under it cannot be opened by
    /// this process, and the answer is to restore the key rather than to carry on.
    /// </exception>
    public byte[] this[string keyId] =>
        keys.TryGetValue(keyId, out byte[]? key)
            ? key
            : throw new CryptographicException(
                $"No key-encryption key with id '{keyId}' is configured under "
                + $"{EnvelopeEncryptionOptions.SectionName}. Material sealed under it cannot be read.");

    /// <summary>
    /// Everything wrong with a configured ring, as sentences. Empty means it is usable.
    /// </summary>
    /// <remarks>
    /// Shared with the options validator, so the check that fails the host at startup and the
    /// check this constructor makes cannot drift apart.
    /// </remarks>
    public static IReadOnlyList<string> Validate(EnvelopeEncryptionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> problems = [];

        if (options.Keys.Count == 0)
        {
            problems.Add(
                $"{EnvelopeEncryptionOptions.SectionName}:Keys is empty. Supply at least one "
                + $"key-encryption key: {KeyLengthBytes} random bytes, base64-encoded.");
        }

        foreach ((string keyId, string encoded) in options.Keys)
        {
            if (string.IsNullOrWhiteSpace(keyId))
            {
                problems.Add($"{EnvelopeEncryptionOptions.SectionName}:Keys has an entry with no id.");

                continue;
            }

            if (!Convert.TryFromBase64String(encoded ?? string.Empty, new byte[KeyLengthBytes], out int written))
            {
                problems.Add(
                    $"{EnvelopeEncryptionOptions.SectionName}:Keys:{keyId} is not base64 of exactly "
                    + $"{KeyLengthBytes} bytes.");

                continue;
            }

            if (written != KeyLengthBytes)
            {
                problems.Add(
                    $"{EnvelopeEncryptionOptions.SectionName}:Keys:{keyId} decodes to {written} bytes; "
                    + $"a key-encryption key is exactly {KeyLengthBytes}.");
            }
        }

        if (string.IsNullOrWhiteSpace(options.ActiveKeyId))
        {
            problems.Add(
                $"{EnvelopeEncryptionOptions.SectionName}:ActiveKeyId is not set. It names which key "
                + "in Keys new material is wrapped under.");
        }
        else if (!options.Keys.ContainsKey(options.ActiveKeyId))
        {
            problems.Add(
                $"{EnvelopeEncryptionOptions.SectionName}:ActiveKeyId is '{options.ActiveKeyId}', which "
                + "is not a key in Keys.");
        }

        return problems;
    }
}
