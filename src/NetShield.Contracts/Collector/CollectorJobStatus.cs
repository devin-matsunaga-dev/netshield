using System.Text.Json.Serialization;

namespace NetShield.Contracts.Collector;

/// <summary>Where a collector job is in its life.</summary>
/// <remarks>
/// There is no <c>Cancelled</c>. Nothing cancels a job in V1, and a member no code can reach is
/// a state a later reader has to work out the meaning of.
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
    Failed
}
