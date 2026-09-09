using NetShield.Contracts.Collector;

using NetShield.Platform.Results;

namespace NetShield.Inventory.Collector;

/// <summary>
/// Every refusal the job-queue handlers can return, in one place, so the codes a caller branches
/// on are visible together rather than spread across the handlers (CONVENTIONS.md §4).
/// </summary>
/// <remarks>
/// Separate from <c>CollectorErrors</c>, which belongs to the collector's own internal contract
/// and speaks to a machine. These are refusals a person reads on a screen.
/// </remarks>
internal static class CollectorJobErrors
{
    /// <summary>The code a caller sees when the job is not this device's, or is not there.</summary>
    internal const string NotFoundCode = "collector.job-not-found";

    /// <summary>The code a caller sees when the job has moved past the point of cancelling.</summary>
    internal const string NotCancellableCode = "collector.job-not-cancellable";

    /// <summary>
    /// No such job on this device.
    /// </summary>
    /// <remarks>
    /// One error for "no such job" and for "that job belongs to another device", deliberately.
    /// Telling a caller which of the two it was would answer a question about a device they did
    /// not ask about and may not be permitted to see.
    /// </remarks>
    internal static Error NotFound(Guid deviceId, Guid jobId) =>
        Error.NotFound(
            NotFoundCode,
            $"Device {deviceId} has no queued job {jobId}.");

    /// <summary>
    /// The job is past cancelling.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <c>409</c> rather than a <c>404</c>: the job is there and the request is well formed,
    /// but it names a transition the job can no longer make. That is the distinction
    /// CONVENTIONS.md §4 draws between the two codes, and it is the one that tells a screen
    /// whether to say "it is gone" or "it already started".
    /// </para>
    /// <para>
    /// Cancelling is reachable only from <c>Pending</c>. A leased job is being run right now by a
    /// collector that does not know anybody changed their mind, and taking it back would leave
    /// that collector to post a result the API refuses — a worse end than letting a read that is
    /// already open finish.
    /// </para>
    /// </remarks>
    internal static Error NotCancellable(Guid jobId, CollectorJobStatus status) =>
        Error.Conflict(
            NotCancellableCode,
            $"Job {jobId} cannot be cancelled because it is {Describe(status)}.");

    private static string Describe(CollectorJobStatus status) => status switch
    {
        CollectorJobStatus.Leased => "already running",
        CollectorJobStatus.Succeeded => "already finished",
        CollectorJobStatus.Failed => "already finished",
        CollectorJobStatus.Cancelled => "already cancelled",
        _ => "no longer queued"
    };
}
