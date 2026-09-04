using NetShield.Contracts.Inventory;

using NetShield.Platform.Cryptography;

namespace NetShield.Inventory.Credentials;

/// <summary>
/// A set of credentials NetShield can reach devices with.
/// </summary>
/// <remarks>
/// <para>
/// Internal, like <c>Device</c> and for the same reason (ARCHITECTURE.md §4) — and here the rule
/// earns its keep twice over, because the three properties below the describable ones are the
/// sealed material. Nothing outside <c>NetShield.Inventory</c> can name this type, so nothing
/// outside it can hold the ciphertext, let alone ask for the plaintext.
/// </para>
/// <para>
/// The material is stored as one sealed blob rather than a column per secret. A column per
/// secret would mean a migration every time a protocol gained a member, four mostly-null columns
/// at all times, and four places for a future package to forget to encrypt one. One blob has one
/// shape — <c>CredentialMaterialPayload</c> — and one place it is opened.
/// </para>
/// </remarks>
internal sealed class CredentialProfile
{
    /// <summary>UUID v7, so the primary key is also the creation order (CONVENTIONS.md §3).</summary>
    public Guid Id { get; init; }

    /// <summary>What an operator calls it.</summary>
    public required string Name { get; set; }

    /// <summary>
    /// <see cref="Name"/> case-folded, which is what the uniqueness guarantee is actually made
    /// over: "Core switches" and "core switches" are one name to a person choosing between them.
    /// </summary>
    public required string NormalizedName { get; set; }

    /// <summary>What it is for. Free text.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// Which protocol it authenticates. <c>init</c>-only: the kind decides what the sealed
    /// material contains, and a kind that changed would leave the material describing a protocol
    /// this profile no longer claims to be for.
    /// </summary>
    public CredentialKind Kind { get; init; }

    /// <summary>The SNMP v3 security name or the SSH username. Null for SNMP v2c.</summary>
    public string? Username { get; set; }

    /// <summary>The SNMP v3 authentication algorithm. Null for every other kind.</summary>
    public SnmpAuthAlgorithm? AuthAlgorithm { get; set; }

    /// <summary>The SNMP v3 privacy algorithm. Null for every other kind.</summary>
    public SnmpPrivacyAlgorithm? PrivacyAlgorithm { get; set; }

    /// <summary>
    /// Which key-encryption key <see cref="WrappedDataKey"/> is wrapped under. Stored so a
    /// rotation can find the rows still on the previous key.
    /// </summary>
    public required string KeyId { get; set; }

    /// <summary>This profile's data-encryption key, sealed under the key-encryption key.</summary>
    public required byte[] WrappedDataKey { get; set; }

    /// <summary>The material, sealed under the data-encryption key.</summary>
    public required byte[] MaterialCiphertext { get; set; }

    /// <summary>
    /// When the material was last replaced. UTC. The only thing the API says about the secret,
    /// and enough to answer "has this been rotated since" without reading it.
    /// </summary>
    public DateTimeOffset MaterialUpdatedAt { get; set; }

    /// <summary>When the profile was created. UTC.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the row last changed. UTC.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// When the profile was removed, or <see langword="null"/> while it is live. Soft delete, per
    /// CONVENTIONS.md §3 — the audit rows naming this profile still resolve afterwards.
    /// </summary>
    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>The sealed material as the encryptor understands it.</summary>
    public EnvelopeCiphertext Ciphertext => new(KeyId, WrappedDataKey, MaterialCiphertext);

    /// <summary>Replaces the sealed material with a freshly sealed or re-wrapped one.</summary>
    public void SetCiphertext(EnvelopeCiphertext ciphertext)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);

        KeyId = ciphertext.KeyId;
        WrappedDataKey = ciphertext.WrappedDataKey;
        MaterialCiphertext = ciphertext.Payload;
    }

    /// <summary>
    /// What the sealed material is bound to. Passed to the encryptor as additional authenticated
    /// data, so this profile's blob opens for this profile and for nothing else — a row copied
    /// into another profile's columns fails to decrypt rather than handing over the wrong
    /// device's credential.
    /// </summary>
    public static string ContextFor(Guid id) => $"credential-profile:{id:D}";
}
