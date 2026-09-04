namespace NetShield.Inventory.Collector.Contract;

/// <summary>The body of <c>POST /internal/collector/heartbeat</c>.</summary>
/// <remarks>
/// Every member is the collector's own claim about itself. The shared secret proves that a
/// collector is talking, not which one — so nothing here is used to authorize anything, and the
/// row it updates exists to answer "is anything collecting, and does it have room".
/// </remarks>
/// <param name="Name">What this collector calls itself. Stable across its restarts.</param>
/// <param name="Version">Which build it is.</param>
/// <param name="Capacity">How many jobs it can run at once.</param>
/// <param name="Running">How many it is running now.</param>
internal sealed record CollectorHeartbeatRequest(string Name, string? Version, int Capacity, int Running);
