namespace NetShield.Contracts.Inventory;

/// <summary>What a caller sends to create a discovery seed.</summary>
/// <remarks>
/// <see cref="DiscoverySeedDetail.NextRunAt"/> is not on the request. When a seed next runs is
/// something the schedule decides, and a caller who could set it could ask for a sweep of the
/// estate at an arbitrary moment without going through the route that is gated on
/// <c>DiscoveryRun</c> — the same reasoning that keeps <c>state</c> off the device requests.
/// </remarks>
/// <param name="Name">What to call it. Unique among live seeds.</param>
/// <param name="Description">Why it exists, as free text.</param>
/// <param name="Enabled">Whether the schedule runs it. Defaults to on.</param>
/// <param name="Ranges">
/// The CIDR ranges to sweep. A bare address is accepted and read as a single host.
/// </param>
/// <param name="Exclusions">Addresses and ranges inside those that must never be probed.</param>
/// <param name="IntervalMinutes">How often the schedule runs it.</param>
public sealed record CreateDiscoverySeedRequest(
    string Name,
    string? Description,
    bool Enabled,
    IReadOnlyList<string> Ranges,
    IReadOnlyList<string>? Exclusions,
    int IntervalMinutes);
