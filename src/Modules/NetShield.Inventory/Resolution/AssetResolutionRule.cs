using System.Net;

using NetShield.Contracts.Inventory;

namespace NetShield.Inventory.Resolution;

/// <summary>
/// The order between the two things that can claim an address, as one pure function.
/// </summary>
/// <remarks>
/// <para>
/// Separated from <see cref="AssetResolver"/> because this is the part that is easy to get subtly
/// wrong and impossible to notice: a resolution that picks the wrong claimant at a handover
/// boundary is wrong silently, in data nobody re-reads. Everything around it — the cache, the
/// read-through, the queries — is machinery that can be exercised end to end; this is the
/// decision, and it is tested as arithmetic with no database and no Redis in the way.
/// </para>
/// <para>
/// <strong>The rule, in order.</strong>
/// </para>
/// <list type="number">
/// <item><description>
/// A client interval covering the instant, <em>and</em> a live device at that address which
/// already existed then → the <strong>device</strong>. This is the ordinary case for a router's
/// own interface: a neighbouring router's ARP cache reports it, so a client row exists for its
/// MAC, and the asset an operator means is still the device. The MAC travels on the answer as
/// evidence rather than being thrown away.
/// </description></item>
/// <item><description>
/// A client interval covering the instant, with no such device → the <strong>client</strong>.
/// This is what makes a DHCP handover come out right. The device's creation time is what decides
/// the awkward case: an address that was a laptop until a switch was racked there resolves to the
/// laptop for any instant before the device row existed, because a device promoted last Tuesday
/// cannot have been the asset on Monday.
/// </description></item>
/// <item><description>
/// No client interval, but a live device at that address → the <strong>device</strong>, whatever
/// the instant. <c>devices</c> holds one address and no record of what it held before (WP-1.1),
/// so device resolution is current-address-only and this is where that shows. The creation time
/// is a tie-break between two candidates and never a gate on the only one.
/// </description></item>
/// <item><description>
/// Otherwise <strong>unresolved</strong> — which is an answer, not a failure. An address outside
/// the estate is the ordinary case, and ARCHITECTURE.md §6 requires enrichment to land an event
/// with a null asset reference rather than to fail the write.
/// </description></item>
/// </list>
/// </remarks>
internal static class AssetResolutionRule
{
    /// <summary>What <paramref name="history"/> says held <paramref name="address"/> at <paramref name="at"/>.</summary>
    internal static AssetResolution Apply(IPAddress address, DateTimeOffset at, AddressHistory history)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(history);

        string text = address.ToString();
        CachedBinding? binding = history.At(at);
        CachedDevice? device = history.Device;

        if (device is not null && (binding is null || at >= device.CreatedAt))
        {
            return new AssetResolution(
                text,
                at,
                AssetKind.Device,
                device.Id,
                device.Hostname,
                binding?.ClientId,
                binding?.MacAddress,
                binding?.ObservedFrom,
                binding?.ObservedTo);
        }

        if (binding is not null)
        {
            return new AssetResolution(
                text,
                at,
                AssetKind.Client,
                DeviceId: null,
                DeviceHostname: null,
                binding.ClientId,
                binding.MacAddress,
                binding.ObservedFrom,
                binding.ObservedTo);
        }

        return new AssetResolution(
            text,
            at,
            AssetKind.Unresolved,
            DeviceId: null,
            DeviceHostname: null,
            ClientId: null,
            MacAddress: null,
            ObservedFrom: null,
            ObservedTo: null);
    }
}
