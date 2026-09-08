using System.Net;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NetShield.Contracts.Inventory;
using NetShield.Contracts.Inventory.Events;

using NetShield.Inventory.Persistence;

using NetShield.Platform.Messaging;

namespace NetShield.Inventory.Clients;

/// <summary>
/// Folds a set of observations into the client tables, maintaining the interval invariants.
/// </summary>
/// <remarks>
/// <para>
/// <strong>An interval closes only when a conflicting observation supersedes it.</strong> That is
/// the single rule this file exists to enforce, and it is the opposite of what a first reading of
/// "history as closed intervals" suggests, so it is worth being explicit about why.
/// </para>
/// <para>
/// An ARP cache ages an entry out after a few hours and a forwarding database after a few
/// minutes, whether or not the host is still there — a laptop that goes quiet over lunch
/// disappears from both. If absence closed an interval, an ordinary workstation would produce
/// dozens of intervals a day, each one a gap in which <c>ResolveAssetAt</c> answers
/// <em>unresolved</em> for an address that never actually changed hands. So a binding is closed
/// only by evidence that contradicts it: another hardware address holding the same IP, or the
/// same client appearing on a different port of the same device. Everything else moves
/// <c>LastSeenAt</c> and nothing more, which leaves the record saying exactly what NetShield
/// knows — "the last time anything saw 10.0.0.5, it was this laptop, at this time".
/// </para>
/// <para>
/// The consequence is that an open interval can be arbitrarily old, and the honest place to
/// answer that is a retention or idle-expiry policy rather than a guess made here. It is recorded
/// in <c>STATUS.md</c> for the package that writes the policy table.
/// </para>
/// <para>
/// <strong>Every close is stamped at exactly the successor's open.</strong> One instant is
/// captured for the whole application and used for both sides of every handover, so the intervals
/// abut with no gap and no overlap — which is what makes a lookup at any timestamp find exactly
/// one row.
/// </para>
/// <para>
/// It stages changes on the caller's context and saves nothing, in the shape
/// <c>DiscoveryRunLauncher</c> and <c>OutboxEnlistment</c> already use, so that the bindings, the
/// scan row and the events announcing new clients commit together or not at all.
/// </para>
/// </remarks>
internal sealed class ClientObservationApplier(
    InventoryDbContext context,
    OutboxEnlistment outbox,
    ILogger<ClientObservationApplier> logger)
{
    /// <summary>What one application changed, so the caller can invalidate what it must.</summary>
    /// <param name="NewClients">How many hardware addresses were seen for the first time.</param>
    /// <param name="OpenedBindings">How many address intervals were opened.</param>
    /// <param name="ClosedBindings">How many were closed by a conflicting observation.</param>
    /// <param name="MovedPorts">How many clients appeared on a different port of one device.</param>
    /// <param name="ChangedAddresses">
    /// Every address whose intervals moved. The set the cache has to drop, and only those: an
    /// observation that merely confirmed what was already open changes no answer.
    /// </param>
    internal sealed record Applied(
        int NewClients,
        int OpenedBindings,
        int ClosedBindings,
        int MovedPorts,
        IReadOnlySet<IPAddress> ChangedAddresses);

    /// <summary>
    /// Applies one device's observations at <paramref name="now"/>.
    /// </summary>
    /// <param name="deviceId">The device that reported them.</param>
    /// <param name="addresses">The address bindings it saw.</param>
    /// <param name="ports">The port bindings it saw.</param>
    /// <param name="now">The instant every interval opened or closed by this call is stamped at.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<Applied> ApplyAsync(
        Guid deviceId,
        IReadOnlyList<ClientObservation.Address> addresses,
        IReadOnlyList<ClientObservation.Port> ports,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        ArgumentNullException.ThrowIfNull(ports);

        IReadOnlySet<string> macs =
            new HashSet<string>(
                addresses.Select(address => address.MacAddress)
                    .Concat(ports.Select(port => port.MacAddress)),
                StringComparer.Ordinal);

        (Dictionary<string, Client> clients, int created) =
            await EnsureClientsAsync(macs, addresses, now, cancellationToken);

        HashSet<IPAddress> changed = [];

        (int opened, int closed) = await ApplyAddressesAsync(
            deviceId, addresses, clients, now, changed, cancellationToken);

        int moved = await ApplyPortsAsync(deviceId, ports, clients, now, cancellationToken);

        return new Applied(created, opened, closed, moved, changed);
    }

    /// <summary>
    /// Finds or creates a row for every hardware address seen, and moves its last-seen stamp.
    /// </summary>
    /// <remarks>
    /// One query for every address rather than one per address: a distribution switch's
    /// forwarding database is thousands of entries, and a lookup each would be thousands of round
    /// trips per walk.
    ///
    /// Creating a row is safe without a uniqueness race because the outbox dispatcher runs in one
    /// process and delivers one row at a time (WP-0.3), so two walks are never applied
    /// concurrently. The unique index on <c>mac_address</c> is what would catch it if that ever
    /// stopped being true.
    /// </remarks>
    private async Task<(Dictionary<string, Client> Clients, int Created)> EnsureClientsAsync(
        IReadOnlySet<string> macs,
        IReadOnlyList<ClientObservation.Address> addresses,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        Dictionary<string, Client> clients = new(StringComparer.Ordinal);

        if (macs.Count == 0)
        {
            return (clients, 0);
        }

        List<string> wanted = [.. macs];

        foreach (Client existing in await context.Clients
            .Where(client => wanted.Contains(client.MacAddress))
            .ToListAsync(cancellationToken))
        {
            existing.LastSeenAt = now;
            existing.UpdatedAt = now;
            clients[existing.MacAddress] = existing;
        }

        // The address a new client is announced with, so ClientDiscovered can say where it was
        // rather than only that it exists. A client seen only in a forwarding database has none.
        Dictionary<string, ClientObservation.Address> firstAddress = new(StringComparer.Ordinal);

        foreach (ClientObservation.Address address in addresses)
        {
            firstAddress.TryAdd(address.MacAddress, address);
        }

        int created = 0;

        foreach (string mac in macs)
        {
            if (clients.ContainsKey(mac))
            {
                continue;
            }

            Client client = new()
            {
                Id = Guid.CreateVersion7(now),
                MacAddress = mac,
                Oui = MacAddress.Oui(mac),
                LocallyAdministered = MacAddress.IsLocallyAdministered(mac),
                FirstSeenAt = now,
                LastSeenAt = now,
                CreatedAt = now,
                UpdatedAt = now
            };

            context.Clients.Add(client);
            clients[mac] = client;
            created++;

            firstAddress.TryGetValue(mac, out ClientObservation.Address? seen);

            // Once per client, not once per sighting: a switch re-reports every endpoint on it
            // at every walk, and a subscriber should not be woken to be told what it knows.
            outbox.Enlist(
                context,
                new ClientDiscovered(
                    client.Id,
                    mac,
                    seen?.IpAddress.ToString(),
                    seen?.Source ?? ClientObservationSource.MacAddressTable,
                    now));
        }

        return (clients, created);
    }

    /// <summary>Opens, confirms and supersedes address intervals.</summary>
    private async Task<(int Opened, int Closed)> ApplyAddressesAsync(
        Guid deviceId,
        IReadOnlyList<ClientObservation.Address> addresses,
        IReadOnlyDictionary<string, Client> clients,
        DateTimeOffset now,
        HashSet<IPAddress> changed,
        CancellationToken cancellationToken)
    {
        if (addresses.Count == 0)
        {
            return (0, 0);
        }

        List<IPAddress> seen = [.. addresses.Select(address => address.IpAddress).Distinct()];

        // Every currently-open interval for the addresses this walk saw, in one query. The
        // partial unique index guarantees at most one per address, so this dictionary cannot lose
        // a row to a key collision.
        Dictionary<IPAddress, ClientIpBinding> open = await context.ClientIpBindings
            .Where(binding => binding.ObservedTo == null && seen.Contains(binding.IpAddress))
            .ToDictionaryAsync(binding => binding.IpAddress, cancellationToken);

        int opened = 0;
        int closed = 0;
        HashSet<IPAddress> applied = [];

        foreach (ClientObservation.Address address in addresses)
        {
            // A device reporting one address twice in one table — two rows of an ARP cache
            // keyed by different interfaces — is one observation, not two conflicting ones.
            if (!applied.Add(address.IpAddress))
            {
                continue;
            }

            Client client = clients[address.MacAddress];

            if (open.TryGetValue(address.IpAddress, out ClientIpBinding? current))
            {
                if (current.ClientId == client.Id)
                {
                    // The same client still holds it. Confirmation, not a change: the interval
                    // stays open, its last-seen moves, and no cached answer becomes wrong.
                    current.LastSeenAt = now;
                    current.Source = address.Source;
                    current.DeviceId = deviceId;
                    current.UpdatedAt = now;

                    continue;
                }

                // A different hardware address holds it. This is the handover, and closing it at
                // exactly `now` — the instant the successor opens — is what leaves no gap.
                current.ObservedTo = now;
                current.UpdatedAt = now;
                closed++;

                logger.LogInformation(
                    "Address {IpAddress} moved from client {PreviousClientId} to {ClientId}",
                    address.IpAddress,
                    current.ClientId,
                    client.Id);
            }

            context.ClientIpBindings.Add(new ClientIpBinding
            {
                Id = Guid.CreateVersion7(now),
                ClientId = client.Id,
                IpAddress = address.IpAddress,
                Source = address.Source,
                DeviceId = deviceId,
                ObservedFrom = now,
                LastSeenAt = now,
                CreatedAt = now,
                UpdatedAt = now
            });

            opened++;
            changed.Add(address.IpAddress);
        }

        return (opened, closed);
    }

    /// <summary>Opens, confirms and supersedes port intervals, per device.</summary>
    /// <remarks>
    /// Scoped to one device, which is the whole difference from the address side. A client
    /// legitimately has one open port binding on its access switch and one on every switch above
    /// it, because a MAC is learned by every bridge on the path to it — so a walk of one switch
    /// must never close another switch's correct observation.
    /// </remarks>
    private async Task<int> ApplyPortsAsync(
        Guid deviceId,
        IReadOnlyList<ClientObservation.Port> ports,
        IReadOnlyDictionary<string, Client> clients,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (ports.Count == 0)
        {
            return 0;
        }

        List<Guid> ids = [.. ports.Select(port => clients[port.MacAddress].Id).Distinct()];

        Dictionary<Guid, ClientPortBinding> open = await context.ClientPortBindings
            .Where(binding =>
                binding.DeviceId == deviceId
                && binding.ObservedTo == null
                && ids.Contains(binding.ClientId))
            .ToDictionaryAsync(binding => binding.ClientId, cancellationToken);

        int moved = 0;
        HashSet<Guid> applied = [];

        foreach (ClientObservation.Port port in ports)
        {
            Client client = clients[port.MacAddress];

            // One device reporting one MAC on two ports is a table NetShield cannot read as two
            // facts — a bridge forwards to one port per address — so the first entry wins and
            // the rest of the reading is kept rather than the walk being failed over it.
            if (!applied.Add(client.Id))
            {
                continue;
            }

            if (open.TryGetValue(client.Id, out ClientPortBinding? current))
            {
                if (current.IfIndex == port.IfIndex)
                {
                    current.VlanId = port.VlanId;
                    current.MacCountOnPort = port.MacCountOnPort;
                    current.Source = port.Source;
                    current.LastSeenAt = now;
                    current.UpdatedAt = now;

                    continue;
                }

                current.ObservedTo = now;
                current.UpdatedAt = now;
                moved++;
            }

            context.ClientPortBindings.Add(new ClientPortBinding
            {
                Id = Guid.CreateVersion7(now),
                ClientId = client.Id,
                DeviceId = deviceId,
                IfIndex = port.IfIndex,
                VlanId = port.VlanId,
                MacCountOnPort = port.MacCountOnPort,
                Source = port.Source,
                ObservedFrom = now,
                LastSeenAt = now,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        return moved;
    }
}
