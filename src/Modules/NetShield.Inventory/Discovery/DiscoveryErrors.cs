using NetShield.Platform.Results;

namespace NetShield.Inventory.Discovery;

/// <summary>
/// Every refusal the discovery handlers can return, in one place, so the codes a client branches
/// on are visible together rather than spread across the files that raise them
/// (CONVENTIONS.md §4).
/// </summary>
internal static class DiscoveryErrors
{
    /// <summary>The code a caller sees when the device has no SNMP credential to be walked with.</summary>
    internal const string NoSnmpCredentialCode = "discovery.no-snmp-credential";

    /// <summary>The code a caller sees when the device already has a walk queued or running.</summary>
    internal const string WalkOutstandingCode = "discovery.walk-outstanding";

    /// <summary>
    /// A device with no SNMPv2c or SNMPv3 profile assigned cannot be walked.
    /// </summary>
    /// <remarks>
    /// A <c>409</c> rather than a <c>422</c>: nothing is wrong with the request, and the same
    /// request will succeed once a credential has been assigned. The message says what to do,
    /// and names no profile — which credentials exist is behind <c>CredentialsManage</c>.
    /// </remarks>
    internal static Error NoSnmpCredential(Guid deviceId) =>
        Error.Conflict(
            NoSnmpCredentialCode,
            $"Device {deviceId} has no SNMP credential profile assigned, so it cannot be walked. "
            + "Assign an SNMPv2c or SNMPv3 profile to it first.");

    /// <summary>
    /// A walk is already queued or leased for this device.
    /// </summary>
    /// <remarks>
    /// Refusing is what bounds the queue at one outstanding walk per device, the same rule the
    /// reachability schedule applies for the same reason: a collector outage must not be able to
    /// turn a person clicking twice into a backlog nobody will ever run.
    /// </remarks>
    internal static Error WalkOutstanding(Guid deviceId) =>
        Error.Conflict(
            WalkOutstandingCode,
            $"Device {deviceId} already has a walk queued. Wait for it to finish before asking for another.");
}
