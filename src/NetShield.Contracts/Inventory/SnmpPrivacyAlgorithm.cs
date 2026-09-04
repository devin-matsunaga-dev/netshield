using System.Text.Json.Serialization;

namespace NetShield.Contracts.Inventory;

/// <summary>
/// The SNMP v3 privacy algorithm a profile encrypts with, or <see cref="None"/> for
/// <c>authNoPriv</c>.
/// </summary>
/// <remarks>
/// <see cref="None"/> is a member rather than a null, because "this profile authenticates and
/// does not encrypt" is a decision an operator made and a compliance rule will want to read,
/// while an absent value is a question about whether anybody chose.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<SnmpPrivacyAlgorithm>))]
public enum SnmpPrivacyAlgorithm
{
    /// <summary><c>authNoPriv</c> — authenticated, not encrypted.</summary>
    None,

    /// <summary>CBC-DES. Legacy, and weak.</summary>
    Des,

    /// <summary>CFB128-AES-128.</summary>
    Aes128,

    /// <summary>CFB128-AES-192.</summary>
    Aes192,

    /// <summary>CFB128-AES-256.</summary>
    Aes256
}
