using System.Text.Json.Serialization;

namespace NetShield.Contracts.Collector;

/// <summary>Where a collector job is in its life.</summary>
/// <remarks>
/// <para>
/// <see cref="Cancelled"/> was added in WP-2.5, at the human's instruction, through the seam
/// WP-1.3 named when it wrote that there was none: <em>"nothing cancels a job in V1"</em> was
/// true until an operator needed to stop one, and the first time somebody queues a walk against
/// the wrong device — or against a device the collector cannot reach — they need a way to take
/// it back out of the queue.
/// </para>
/// <para>
/// It is a terminal state and it is reachable only from <see cref="Pending"/>. A
/// <see cref="Leased"/> job is already being run by a collector that has no idea anybody changed
/// their mind; cancelling it would leave the collector to post a result the API then refuses,
/// which is a worse outcome than letting a read it has already started finish. Revoking a lease
/// is a change to the collector contract rather than to this enum, and nothing asks for it yet.
/// </para>
/// <para>
/// Nothing in the claim query needs to know about it. A collector claims rows matching
/// <c>status = Pending OR (status = Leased AND lease expired)</c>, so a cancelled job simply
/// stops being a row anything selects.
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<CollectorJobStatus>))]
public enum CollectorJobStatus
{
    /// <summary>Queued and waiting for its due time, or for a collector to claim it.</summary>
    Pending,

    /// <summary>Claimed by a collector, and its lease has not yet expired.</summary>
    Leased,

    /// <summary>The collector ran it and reported success.</summary>
    Succeeded,

    /// <summary>The collector ran it and reported a failure, or it exhausted its attempts.</summary>
    Failed,

    /// <summary>
    /// A person took it out of the queue before any collector claimed it. Terminal: the row is
    /// kept, because what was queued and then withdrawn is part of the record of what happened.
    /// </summary>
    Cancelled
}
