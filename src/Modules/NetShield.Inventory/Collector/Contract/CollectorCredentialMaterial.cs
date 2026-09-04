using System.Text.Json.Serialization;

namespace NetShield.Inventory.Collector.Contract;

/// <summary>
/// The plaintext of one credential, as it travels to the collector.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Internal, and it must stay internal.</strong> ARCHITECTURE.md §7 says a device
/// credential is decrypted in the API, delivered over TLS and held in memory only; a type that
/// cannot be named outside <c>NetShield.Inventory</c> cannot be held by code that was not written
/// to those rules. It is deliberately not in <c>NetShield.Contracts</c> for exactly the reason
/// <c>CredentialMaterialPayload</c> is not: a public type carrying a plaintext credential is the
/// thing WP-1.2's structural gate exists to make impossible.
/// </para>
/// <para>
/// It is also a third shape rather than a reuse of the other two. <c>CredentialMaterial</c> is
/// what a caller may send the API, <c>CredentialMaterialPayload</c> is what is written to the
/// column, and this is what the collector is told — three contracts with three lifetimes, and
/// tying any two of them together makes a change to one a silent break of another.
/// </para>
/// </remarks>
internal sealed record CollectorCredentialMaterial
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
}
