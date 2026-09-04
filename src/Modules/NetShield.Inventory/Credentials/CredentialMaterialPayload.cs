using System.Text.Json.Serialization;

using NetShield.Contracts.Inventory;

namespace NetShield.Inventory.Credentials;

/// <summary>
/// The plaintext material, as it is shaped inside the sealed blob.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately its own type rather than <see cref="CredentialMaterial"/> from
/// <c>Contracts</c>, even though the members are the same today. The contract type describes what
/// a caller may send and is free to change with the API; this one describes bytes already written
/// to a column that must still open in five years. Tying the two together would make a rename on
/// the wire a silent corruption at rest, and every member here carries an explicit
/// <see cref="JsonPropertyNameAttribute"/> for the same reason.
/// </para>
/// <para>
/// It never leaves the module and it is absent from every serializer context the API uses. The
/// only things that construct one are the seal path and the decrypt path, both below.
/// </para>
/// </remarks>
internal sealed record CredentialMaterialPayload
{
    /// <summary>The SNMP v2c community string.</summary>
    [JsonPropertyName("community")]
    public string? Community { get; init; }

    /// <summary>The SNMP v3 authentication pass phrase.</summary>
    [JsonPropertyName("authPassword")]
    public string? AuthPassword { get; init; }

    /// <summary>The SNMP v3 privacy pass phrase.</summary>
    [JsonPropertyName("privacyPassword")]
    public string? PrivacyPassword { get; init; }

    /// <summary>The SSH password.</summary>
    [JsonPropertyName("password")]
    public string? Password { get; init; }

    /// <summary>The SSH private key, in PEM form.</summary>
    [JsonPropertyName("privateKey")]
    public string? PrivateKey { get; init; }

    /// <summary>The pass phrase protecting the private key.</summary>
    [JsonPropertyName("privateKeyPassword")]
    public string? PrivateKeyPassword { get; init; }

    /// <summary>
    /// Reads what a caller sent, trimming nothing but whitespace-only values to null. A private
    /// key's own leading and trailing whitespace is left alone — PEM is line-oriented and
    /// trimming it is how a key stops parsing.
    /// </summary>
    public static CredentialMaterialPayload From(CredentialMaterial material)
    {
        ArgumentNullException.ThrowIfNull(material);

        return new CredentialMaterialPayload
        {
            Community = Clean(material.Community),
            AuthPassword = Clean(material.AuthPassword),
            PrivacyPassword = Clean(material.PrivacyPassword),
            Password = Clean(material.Password),
            PrivateKey = string.IsNullOrWhiteSpace(material.PrivateKey) ? null : material.PrivateKey,
            PrivateKeyPassword = Clean(material.PrivateKeyPassword)
        };
    }

    /// <summary>
    /// A secret that arrived as whitespace is absent, not blank. It is not trimmed otherwise: a
    /// pass phrase may legitimately begin or end with a space, and silently changing one is how a
    /// credential that was typed correctly stops working.
    /// </summary>
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
