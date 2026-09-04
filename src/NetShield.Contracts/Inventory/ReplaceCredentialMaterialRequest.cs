namespace NetShield.Contracts.Inventory;

/// <summary>
/// What a caller supplies to replace a profile's secret half, leaving everything else alone.
/// </summary>
/// <remarks>
/// The profile's kind decides which members of <see cref="Material"/> are required. The kind is
/// not on this request: it is a property of the profile being rotated, and accepting one here
/// would let a caller assert a kind the stored profile does not have.
/// </remarks>
/// <param name="Material">The new material. Required.</param>
public sealed record ReplaceCredentialMaterialRequest(CredentialMaterial Material);
