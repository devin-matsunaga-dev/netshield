using System.Text.Json.Serialization;

namespace NetShield.Contracts.Inventory;

/// <summary>Where a discovery run is in its life.</summary>
/// <remarks>
/// <para>
/// A run is a fan-out: it queues one sweep job per chunk of its ranges and is finished when
/// every one of those jobs has reported. The three terminal members say how many of them got
/// through, because a run that swept nine chunks and failed the tenth has found something real
/// and has also missed a tenth of the estate — reporting that as either success or failure
/// alone would be a lie in one direction.
/// </para>
/// <para>
/// Serialised as its name rather than its ordinal (WP-0.4).
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<DiscoveryRunStatus>))]
public enum DiscoveryRunStatus
{
    /// <summary>Queued, and no sweep job has reported yet.</summary>
    Pending,

    /// <summary>Some sweep jobs have reported and some have not.</summary>
    Running,

    /// <summary>Every sweep job reported, and every one of them succeeded.</summary>
    Completed,

    /// <summary>Every sweep job reported, and some of them failed.</summary>
    PartiallyFailed,

    /// <summary>Every sweep job reported, and every one of them failed.</summary>
    Failed
}
