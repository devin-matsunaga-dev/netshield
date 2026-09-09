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

    /// <summary>The code a caller sees when no device carries the VLAN they asked for.</summary>
    internal const string VlanNotFoundCode = "topology.vlan-not-found";

    /// <summary>
    /// No live device is carrying that VLAN.
    /// </summary>
    /// <remarks>
    /// A VLAN exists in NetShield because a device reported it, so "no device carries it" and
    /// "there is no such VLAN" are the same statement. A VLAN every switch has stopped carrying
    /// is gone from the inventory and its <c>device_vlans</c> rows survive as history.
    /// </remarks>
    internal static Error VlanNotFound(int vlanId) =>
        Error.NotFound(
            VlanNotFoundCode,
            $"No device is carrying VLAN {vlanId}.");

    /// <summary>The code a caller sees when a VLAN id is outside the range 802.1Q admits.</summary>
    internal const string VlanIdOutOfRangeCode = "topology.vlan-id-out-of-range";

    /// <summary>
    /// The VLAN id is not one. 802.1Q's <c>VlanIndex</c> is 1 to 4094.
    /// </summary>
    /// <remarks>
    /// A 400 rather than a 404, because a caller asking for VLAN 9000 has made a different kind
    /// of mistake from one asking for a VLAN nothing carries, and only one of the two is worth
    /// them retrying with a different number.
    /// </remarks>
    internal static Error VlanIdOutOfRange(int vlanId) =>
        Error.Validation(
            VlanIdOutOfRangeCode,
            $"{vlanId} is not a VLAN id. A VLAN id is between {TopologyLimits.MinVlanId} "
            + $"and {TopologyLimits.MaxVlanId}.");

    /// <summary>The code a caller sees when the graph's depth argument is not usable.</summary>
    internal const string GraphDepthInvalidCode = "topology.graph-depth-invalid";

    /// <summary>
    /// A depth was asked for outside the range the graph walks.
    /// </summary>
    /// <remarks>
    /// The bound is deliberate rather than defensive. The walk over <c>device_adjacencies</c>
    /// exists to choose what to draw, and an unbounded one would be the multi-hop traversal
    /// <c>ARCHITECTURE.md</c> §1 rules out.
    /// </remarks>
    internal static Error GraphDepthOutOfRange(int depth) =>
        Error.Validation(
            GraphDepthInvalidCode,
            $"depth must be between 0 and {TopologyLimits.MaxGraphDepth}.",
            new Dictionary<string, string[]>
            {
                ["depth"] =
                [
                    $"Must be between 0 and {TopologyLimits.MaxGraphDepth}. "
                    + $"The default when a root is given is {TopologyLimits.DefaultGraphDepth}."
                ]
            });

    /// <summary>
    /// A depth was given with no root to measure it from.
    /// </summary>
    /// <remarks>
    /// Refused rather than ignored. A caller who sends <c>depth=2</c> and no root has asked for
    /// a bounded neighbourhood; silently answering with the whole estate would be the largest
    /// possible answer to a request for a small one.
    /// </remarks>
    internal static Error GraphDepthWithoutRoot() =>
        Error.Validation(
            GraphDepthInvalidCode,
            "depth is measured from a root, so rootDeviceId is required with it.",
            new Dictionary<string, string[]>
            {
                ["depth"] = ["Send rootDeviceId as well, or leave depth off to read the whole estate."]
            });

    /// <summary>The code a caller sees when the graph root is not a device.</summary>
    internal const string GraphRootNotFoundCode = "topology.graph-root-not-found";

    /// <summary>
    /// The device to centre the graph on does not exist, or has been removed.
    /// </summary>
    /// <remarks>
    /// A 404 rather than an empty graph, because a caller who asked to centre on a device and got
    /// nothing back cannot tell "that device has no links" from "that device is not there", and
    /// only one of the two is worth acting on.
    /// </remarks>
    internal static Error GraphRootNotFound(Guid deviceId) =>
        Error.NotFound(
            GraphRootNotFoundCode,
            $"Device {deviceId} is not a device the graph can be centred on.");

    private static string Describe(TopologyWalkKind walk) => walk switch
    {
        TopologyWalkKind.Routes => "routing-table read",
        TopologyWalkKind.Vlans => "VLAN read",
        _ => "neighbour walk"
    };
}
