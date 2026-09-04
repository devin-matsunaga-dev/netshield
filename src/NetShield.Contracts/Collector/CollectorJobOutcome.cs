using System.Text.Json.Serialization;

namespace NetShield.Contracts.Collector;

/// <summary>How a collector says a job ended.</summary>
/// <remarks>
/// Two members, because the collector is a dumb producer (ARCHITECTURE.md §2): it reports that
/// it managed to do the work or that it did not, and every judgement about what that means
/// belongs to the API.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<CollectorJobOutcome>))]
public enum CollectorJobOutcome
{
    /// <summary>The work was done and the result is attached.</summary>
    Succeeded,

    /// <summary>The work was not done. The reason is attached; it is never a credential.</summary>
    Failed
}
