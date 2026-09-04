using System.Text.Json.Serialization;

namespace NetShield.Contracts.Inventory;

/// <summary>
/// The kinds of device credential NetShield stores. One member per protocol NetShield can
/// authenticate with, at the grain SPEC.md §2 draws them at: SNMP for polling and discovery,
/// SSH for config retrieval.
/// </summary>
/// <remarks>
/// <para>
/// The kind decides which secret members a profile carries and is fixed when the profile is
/// created. Changing it would leave the stored material describing a protocol the profile no
/// longer claims to be for, which is a rename of an aggregate rather than an edit to one.
/// </para>
/// <para>
/// Every member here is read-only in use. SPEC.md §3 defers every write to a network device and
/// ARCHITECTURE.md §1 makes that architectural, so an SSH credential exists to run the
/// <c>show</c>-class commands a config backup needs and an SNMP credential exists to walk.
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<CredentialKind>))]
public enum CredentialKind
{
    /// <summary>SNMP v2c, authenticated by a community string alone.</summary>
    SnmpV2c,

    /// <summary>SNMP v3 with a security name, an authentication pass phrase, and optional privacy.</summary>
    SnmpV3,

    /// <summary>SSH with a username and a password.</summary>
    SshPassword,

    /// <summary>SSH with a username and a private key, optionally protected by a pass phrase.</summary>
    SshKey
}
