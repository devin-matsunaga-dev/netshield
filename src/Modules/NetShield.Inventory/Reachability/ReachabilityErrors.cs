using NetShield.Platform.Results;

namespace NetShield.Inventory.Reachability;

/// <summary>
/// Every refusal the reachability read path can return (CONVENTIONS.md §4).
/// </summary>
internal static class ReachabilityErrors
{
    /// <summary>The code a caller sees when nothing has ever scheduled a probe of the device.</summary>
    internal const string NotFoundCode = "reachability.not-found";

    /// <summary>
    /// The device exists and has no reachability row. That is an ordinary state — the schedule
    /// creates the row on the first probe it queues — and it is not the same answer as a device
    /// that is not there.
    /// </summary>
    internal static Error NotFound(Guid deviceId) =>
        Error.NotFound(NotFoundCode, $"Device {deviceId} has not been probed yet.");
}
