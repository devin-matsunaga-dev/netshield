namespace NetShield.Platform.Time;

/// <summary>
/// The current time, injected rather than read from a static. Everything NetShield stores is
/// UTC (CONVENTIONS.md §3), and everything that decides on an interval — a lease that expired,
/// a device that has been silent too long, a token past its lifetime — is testable only if the
/// clock can be replaced.
/// </summary>
public interface IClock
{
    /// <summary>The current instant, in UTC. Never a local time.</summary>
    DateTimeOffset UtcNow { get; }
}
