using NetShield.Platform.Results;

namespace NetShield.Inventory.Credentials;

/// <summary>
/// The decrypt path: the one way a stored credential becomes plaintext.
/// </summary>
/// <remarks>
/// <para>
/// Internal to <c>NetShield.Inventory</c>, and it has no HTTP surface in WP-1.2. The work package
/// says the decrypt path is callable only from the collector-job endpoint, and that endpoint is
/// WP-1.3's to design (ARCHITECTURE.md §7) — publishing one now would both widen the attack
/// surface and settle a contract this package does not own. Internal is the narrowest thing that
/// still lets WP-1.3 compose it, and widening it will be a deliberate line in that diff rather
/// than something already done.
/// </para>
/// <para>
/// It resolves one named profile and does not choose between a device's several. Which credential
/// a given job should use is a scheduling decision, and WP-1.3 and WP-1.6 own it; a resolver that
/// guessed would decrypt more than the caller needed in order to make the guess.
/// </para>
/// </remarks>
internal interface ICredentialResolver
{
    /// <summary>
    /// Opens the material of one live profile.
    /// </summary>
    /// <returns>
    /// The credential, or a not-found refusal when there is no live profile with that id. It
    /// throws rather than refusing when the row exists and will not open — that is a wrong key
    /// ring or an altered row, and neither is something the caller asked for wrongly.
    /// </returns>
    Task<Result<ResolvedCredential>> ResolveAsync(Guid credentialProfileId, CancellationToken cancellationToken);

    /// <summary>
    /// The live profiles assigned to a device, by id and kind and nothing else. Nothing is
    /// decrypted: this is what a caller picks from before asking for one.
    /// </summary>
    Task<Result<IReadOnlyList<CredentialAssignment>>> ForDeviceAsync(
        Guid deviceId,
        CancellationToken cancellationToken);
}
