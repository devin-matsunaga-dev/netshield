using NetShield.Contracts.Inventory;

using NetShield.Platform.Results;

namespace NetShield.Inventory.Topology;

/// <summary>
/// Every refusal the topology handlers can return, in one place, so the codes a caller branches
/// on are visible together rather than spread across the handlers (CONVENTIONS.md §4).
/// </summary>
internal static class TopologyErrors
{
    /// <summary>The code a caller sees when a topology walk is already queued for the device.</summary>
    internal const string WalkOutstandingCode = "topology.walk-outstanding";

    /// <summary>The code a caller sees when a device has never had its topology read.</summary>
    internal const string ScanNotFoundCode = "topology.scan-not-found";

    /// <summary>
    /// A <c>Discover</c> for this device is already queued or leased.
    /// </summary>
    /// <remarks>
    /// The same rule the fingerprint and client walks apply, and for a reason of its own here: an
    /// edge is withdrawn when a complete reading no longer contains it, so two walks of one
    /// device applied in whichever order they came back would have each withdrawing what the
    /// other had just established.
    /// </remarks>
    internal static Error WalkOutstanding(Guid deviceId, TopologyWalkKind walk) =>
        Error.Conflict(
            WalkOutstandingCode,
            $"A {Describe(walk)} of device {deviceId} is already queued.");

    /// <summary>
    /// The device exists and nothing has read its topology.
    /// </summary>
    /// <remarks>
    /// Distinct from <c>device.not-found</c>, the way WP-1.7 made the fingerprint route's absence
    /// distinct: only one of the two is something an operator can act on, and the action is to
    /// walk it.
    /// </remarks>
    internal static Error ScanNotFound(Guid deviceId) =>
        Error.NotFound(
            ScanNotFoundCode,
            $"Device {deviceId} has not had its topology read yet.");

    private static string Describe(TopologyWalkKind walk) => walk switch
    {
        TopologyWalkKind.Routes => "routing-table read",
        _ => "neighbour walk"
    };
}
