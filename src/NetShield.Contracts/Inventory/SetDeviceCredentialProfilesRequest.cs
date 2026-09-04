namespace NetShield.Contracts.Inventory;

/// <summary>
/// The complete set of credential profiles a device should be assigned, replacing whatever it
/// had.
/// </summary>
/// <remarks>
/// Whole-set replacement rather than an add and a remove endpoint, for the reason WP-1.1 chose
/// PUT over PATCH: the request then says what is true afterwards, and two operators editing the
/// same device cannot interleave into a set neither of them asked for.
/// </remarks>
/// <param name="CredentialProfileIds">
/// The profiles to assign. An empty list unassigns everything. Duplicates are collapsed; a
/// profile that does not exist, or has been removed, is refused.
/// </param>
public sealed record SetDeviceCredentialProfilesRequest(IReadOnlyList<Guid> CredentialProfileIds);
