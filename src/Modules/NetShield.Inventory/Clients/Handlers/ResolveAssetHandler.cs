using System.Net;

using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory;

using NetShield.Inventory.Resolution;

using NetShield.Platform.Authorization;
using NetShield.Platform.Results;
using NetShield.Platform.Time;

namespace NetShield.Inventory.Clients.Handlers;

/// <summary>
/// The read surface of <c>ResolveAssetAt</c>: what held an address at an instant.
/// </summary>
/// <remarks>
/// <para>
/// The resolver itself is internal to the module, in the shape the credential resolver has been
/// in since WP-1.2. This route is not the same kind of thing: a credential resolution hands
/// out a secret and this one hands out an attribution, so the reasoning that kept one off the
/// API surface says nothing about the other. What it gives is the ability to check an
/// attribution — "this flow was blamed on that host; was it?" — which is the question somebody
/// asks the first time a Phase 4 report looks wrong, and the only way a person can verify the
/// handover behaviour by hand.
/// </para>
/// <para>
/// Behind <c>InventoryRead</c>, because it answers a question about the inventory and reveals
/// nothing a client list does not.
/// </para>
/// </remarks>
internal sealed class ResolveAssetHandler(
    IAssetResolver resolver,
    IResourceGuard guard,
    IClock clock)
{
    /// <summary>
    /// Resolves <paramref name="ipAddress"/> at <paramref name="at"/>, defaulting to now.
    /// </summary>
    public async Task<Result<AssetResolution>> HandleAsync(
        string? ipAddress,
        DateTimeOffset? at,
        CancellationToken cancellationToken)
    {
        Result permitted = guard.Require(Permission.InventoryRead, GetClientListHandler.ResourceType);

        if (!permitted.IsSuccess)
        {
            return Result<AssetResolution>.Failure(permitted.Error);
        }

        if (!IPAddress.TryParse(ipAddress, out IPAddress? address))
        {
            return ClientErrors.InvalidAddress(ipAddress);
        }

        DateTimeOffset now = clock.UtcNow;
        DateTimeOffset instant = (at ?? now).ToUniversalTime();

        // Every open interval covers every future instant, so the query would answer happily —
        // with the current holder, dressed as a fact about a moment that has not happened.
        if (instant > now)
        {
            return ClientErrors.FutureInstant();
        }

        return await resolver.ResolveAtAsync(address, instant, cancellationToken);
    }
}
