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

    /// <summary>The code a caller sees when a range or exclusion is not an address or a CIDR block.</summary>
    internal const string InvalidCidrCode = "discovery.invalid-cidr";

    /// <summary>The code a caller sees when another live seed already has the name.</summary>
    internal const string SeedNameTakenCode = "discovery.seed-name-taken";

    /// <summary>The code a caller sees when the seed already has a run in flight.</summary>
    internal const string RunInFlightCode = "discovery.run-in-flight";

    /// <summary>The code a caller sees when a seed's ranges resolve to nothing to sweep.</summary>
    internal const string NothingToSweepCode = "discovery.nothing-to-sweep";

    /// <summary>The code a caller sees when a candidate has already been decided about.</summary>
    internal const string CandidateSettledCode = "discovery.candidate-settled";

    /// <summary>The code a caller sees when the address is already on the ignore list.</summary>
    internal const string IgnoreExistsCode = "discovery.ignore-exists";

    /// <summary>A range, exclusion or ignore entry that is not an address or a CIDR block.</summary>
    /// <remarks>
    /// It names the value back, because the caller wrote it and there is nothing sensitive in a
    /// malformed address. A prefix longer than the family allows lands here too.
    /// </remarks>
    internal static Error InvalidCidr(string value) =>
        Error.Validation(
            InvalidCidrCode,
            $"'{value}' is not an IP address or a CIDR block.");

    /// <summary>A seed does not exist, or has been removed.</summary>
    internal static Error SeedNotFound(Guid seedId) =>
        Error.NotFound("discovery.seed-not-found", $"Discovery seed {seedId} was not found.");

    /// <summary>Another live seed already holds the name.</summary>
    /// <remarks>
    /// Names are unique among live seeds so that a run history is readable: a run records the
    /// name the seed had when it started, and two seeds called "Core VLANs" would make that
    /// record ambiguous rather than merely untidy.
    /// </remarks>
    internal static Error SeedNameTaken(string name) =>
        Error.Conflict(SeedNameTakenCode, $"A discovery seed called '{name}' already exists.");

    /// <summary>The seed already has a run that has not finished.</summary>
    /// <remarks>
    /// The same rule the on-demand walk applies to a device, for the same reason: a person
    /// clicking twice must not become two runs sweeping one range and interleaving their
    /// candidates. The schedule skips a seed in this state rather than being refused by it.
    /// </remarks>
    internal static Error RunInFlight(Guid seedId) =>
        Error.Conflict(
            RunInFlightCode,
            $"Discovery seed {seedId} already has a run in progress. Wait for it to finish before starting another.");

    /// <summary>Every address the seed names is excluded, so there is nothing to probe.</summary>
    internal static Error NothingToSweep(Guid seedId) =>
        Error.Unprocessable(
            NothingToSweepCode,
            $"Discovery seed {seedId} has no addresses left to sweep once its exclusions are applied.");

    /// <summary>A candidate does not exist.</summary>
    internal static Error CandidateNotFound(Guid candidateId) =>
        Error.NotFound(
            "discovery.candidate-not-found",
            $"Discovery candidate {candidateId} was not found.");

    /// <summary>The candidate has already been promoted or ignored.</summary>
    /// <remarks>
    /// A conflict rather than a validation failure: the request is well formed and was right
    /// until somebody else acted on the same candidate, which is exactly what two operators
    /// working the same review list will do.
    /// </remarks>
    internal static Error CandidateSettled(Guid candidateId) =>
        Error.Conflict(
            CandidateSettledCode,
            $"Discovery candidate {candidateId} has already been promoted or ignored.");

    /// <summary>An ignore entry does not exist.</summary>
    internal static Error IgnoreNotFound(Guid ignoreId) =>
        Error.NotFound("discovery.ignore-not-found", $"Discovery ignore entry {ignoreId} was not found.");

    /// <summary>The address or range is already ignored.</summary>
    internal static Error IgnoreExists(string cidr) =>
        Error.Conflict(IgnoreExistsCode, $"'{cidr}' is already on the discovery ignore list.");

    /// <summary>A run does not exist.</summary>
    internal static Error RunNotFound(Guid runId) =>
        Error.NotFound("discovery.run-not-found", $"Discovery run {runId} was not found.");
}
