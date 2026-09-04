using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using NetShield.Contracts.Inventory;

using NetShield.Inventory.Persistence;

namespace NetShield.Inventory.Discovery;

/// <summary>
/// Chooses which of a device's SNMP credential profiles a job should be run with.
/// </summary>
/// <remarks>
/// <para>
/// A job names exactly one profile and the lease opens exactly that one, so something has to
/// choose. WP-1.5 made that choice inside <c>QueueDeviceWalkHandler</c> with the order written
/// into the file — SNMPv3, then SNMPv2c — and recorded that WP-1.6 owned making it configurable.
/// This is that, and it is a service of its own because the on-demand walk is no longer the only
/// caller: anything that queues a walk has to make the same choice the same way.
/// </para>
/// <para>
/// The order comes from <c>DiscoveryOptions.CredentialKindOrder</c>, which is an order over
/// kinds and not a list of profiles — see that member for why the difference matters to the
/// <c>CredentialsManage</c> boundary. After it, the earliest assignment wins, and after that the
/// id, so two profiles a device gained in the same instant still resolve to one answer rather
/// than to whichever row the database returned first.
/// </para>
/// <para>
/// A soft-deleted profile is never chosen. A profile an operator revoked must not keep reaching
/// a collector, which is the same rule the lease applies one step later (WP-1.3).
/// </para>
/// </remarks>
internal sealed class SnmpCredentialSelector(
    InventoryDbContext context,
    IOptions<DiscoveryOptions> options)
{
    /// <summary>The kinds an SNMP walk can be run with, whatever order they are configured in.</summary>
    private static readonly CredentialKind[] SnmpKinds = [CredentialKind.SnmpV3, CredentialKind.SnmpV2c];

    /// <summary>
    /// The device's SNMP credential profile, or <see langword="null"/> if it has none.
    /// </summary>
    public async Task<Guid?> ChooseAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        List<CredentialKind> order = Order();

        var candidates = await (
            from assignment in context.DeviceCredentialProfiles
            join profile in context.CredentialProfiles
                on assignment.CredentialProfileId equals profile.Id
            where assignment.DeviceId == deviceId
                && profile.DeletedAt == null
                && SnmpKinds.Contains(profile.Kind)
            select new { profile.Id, profile.Kind, assignment.CreatedAt })
            .ToListAsync(cancellationToken);

        // Ordered here rather than in SQL: the ranking is the position of a kind in a
        // configured list, which is not something the database knows about, and a device has at
        // most a handful of profiles.
        return candidates
            .Where(candidate => order.Contains(candidate.Kind))
            .OrderBy(candidate => order.IndexOf(candidate.Kind))
            .ThenBy(candidate => candidate.CreatedAt)
            .ThenBy(candidate => candidate.Id)
            .Select(candidate => (Guid?)candidate.Id)
            .FirstOrDefault();
    }

    /// <summary>
    /// The configured order, reduced to the kinds an SNMP walk can actually use.
    /// </summary>
    /// <remarks>
    /// A configuration naming <c>SshPassword</c> is not an error worth failing a host over — it
    /// says nothing about which SNMP credential to prefer, so it says nothing. A configuration
    /// naming no SNMP kind at all leaves every device unwalkable, which the endpoint reports as
    /// "this device has no SNMP credential"; that is the honest answer, because with that
    /// configuration it has none NetShield will use.
    /// </remarks>
    private List<CredentialKind> Order() =>
        [.. options.Value.ResolvedCredentialKindOrder.Where(SnmpKinds.Contains).Distinct()];
}
