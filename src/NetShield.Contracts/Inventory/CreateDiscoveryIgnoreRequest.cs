namespace NetShield.Contracts.Inventory;

/// <summary>What a caller sends to add an address or range to the ignore list.</summary>
/// <param name="Cidr">
/// The address or range. A bare address is accepted and read as a single host.
/// </param>
/// <param name="Reason">Why it is being ignored, as free text.</param>
public sealed record CreateDiscoveryIgnoreRequest(string Cidr, string? Reason);
