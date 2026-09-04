namespace NetShield.Inventory.Endpoints;

/// <summary>
/// The part of a seed request that says what to sweep, so that one validator can check both the
/// create and the update shape.
/// </summary>
/// <remarks>
/// Both members are nullable here although neither is on the request shapes, because a request
/// arrives as JSON and a caller can send <c>null</c> for anything. A validator that assumed
/// otherwise would throw where it is supposed to report.
/// </remarks>
/// <param name="Ranges">The CIDR ranges to sweep.</param>
/// <param name="Exclusions">Blocks inside them that must never be probed.</param>
internal sealed record SeedRanges(IReadOnlyList<string>? Ranges, IReadOnlyList<string>? Exclusions);
