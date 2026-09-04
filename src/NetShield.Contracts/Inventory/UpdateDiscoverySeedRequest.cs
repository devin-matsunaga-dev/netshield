namespace NetShield.Contracts.Inventory;

/// <summary>What a caller sends to replace a discovery seed.</summary>
/// <remarks>
/// Whole-resource replacement, like every other update in this module (WP-1.1): an omitted
/// optional member is then unambiguous, and a merge shape cannot express clearing a value
/// without a null-versus-absent distinction the JSON contract does not carry.
/// </remarks>
/// <param name="Name">What to call it. Unique among live seeds.</param>
/// <param name="Description">Why it exists, as free text.</param>
/// <param name="Enabled">Whether the schedule runs it.</param>
/// <param name="Ranges">The CIDR ranges to sweep.</param>
/// <param name="Exclusions">Addresses and ranges inside those that must never be probed.</param>
/// <param name="IntervalMinutes">How often the schedule runs it.</param>
public sealed record UpdateDiscoverySeedRequest(
    string Name,
    string? Description,
    bool Enabled,
    IReadOnlyList<string> Ranges,
    IReadOnlyList<string>? Exclusions,
    int IntervalMinutes);
