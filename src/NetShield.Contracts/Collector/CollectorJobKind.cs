using System.Text.Json.Serialization;

namespace NetShield.Contracts.Collector;

/// <summary>
/// The kinds of work the API schedules onto <c>netshield-collector</c>.
/// </summary>
/// <remarks>
/// <para>
/// The three from ARCHITECTURE.md §7, and no more. Every one of them reads: a poll asks a device
/// for counters, a discovery asks a range who is there, a config fetch asks a device for its
/// running configuration. There is no member for a write and there will not be one — SPEC.md §3
/// defers every write to a network device, and a kind the collector could not refuse is the
/// first half of building one by accident.
/// </para>
/// <para>
/// Serialised and stored as its name rather than its ordinal, so that adding a kind cannot
/// renumber a row already queued.
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<CollectorJobKind>))]
public enum CollectorJobKind
{
    /// <summary>Read metrics from one device — ICMP reachability, SNMP counters.</summary>
    Poll,

    /// <summary>Find what is on a range or walk one device's MIBs.</summary>
    Discover,

    /// <summary>Retrieve one device's configuration over SSH, read-only.</summary>
    ConfigFetch
}
