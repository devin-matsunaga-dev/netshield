namespace NetShield.Inventory.Collector.Contract;

/// <summary>
/// The API's answer to a heartbeat: the pacing the collector should adopt.
/// </summary>
/// <remarks>
/// The API owns scheduling (ARCHITECTURE.md §7), and this is where that ownership becomes real:
/// the collector is told how often to ask for work, how long a lease will last and how much it
/// may take, rather than each of those being a setting on the collector that a deployment can
/// get out of step with the server it talks to.
/// </remarks>
/// <param name="AcknowledgedAt">When the API recorded the heartbeat. UTC.</param>
/// <param name="PollSeconds">How often to ask for work.</param>
/// <param name="LeaseSeconds">How long a lease lasts.</param>
/// <param name="MaxJobsPerLease">The most jobs one lease call will hand over.</param>
internal sealed record CollectorHeartbeatAck(
    DateTimeOffset AcknowledgedAt,
    int PollSeconds,
    int LeaseSeconds,
    int MaxJobsPerLease);
