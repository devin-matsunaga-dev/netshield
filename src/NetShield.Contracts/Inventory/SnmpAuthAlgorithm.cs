using System.Text.Json.Serialization;

namespace NetShield.Contracts.Inventory;

/// <summary>
/// The SNMP v3 authentication algorithm a profile's pass phrase is used with (RFC 3414, RFC 7860).
/// </summary>
/// <remarks>
/// <see cref="Md5"/> and <see cref="Sha1"/> are here because real estates still run them and an
/// inventory that cannot describe the network as it is describes nothing. They are not a
/// recommendation, and Phase 7's compliance baselines are where saying so belongs.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<SnmpAuthAlgorithm>))]
public enum SnmpAuthAlgorithm
{
    /// <summary>HMAC-MD5-96. Legacy, and weak.</summary>
    Md5,

    /// <summary>HMAC-SHA-96. Legacy.</summary>
    Sha1,

    /// <summary>HMAC-SHA-224.</summary>
    Sha224,

    /// <summary>HMAC-SHA-256.</summary>
    Sha256,

    /// <summary>HMAC-SHA-384.</summary>
    Sha384,

    /// <summary>HMAC-SHA-512.</summary>
    Sha512
}
