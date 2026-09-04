using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using NetShield.Inventory.Collector.Contract;
using NetShield.Inventory.Persistence;

using NetShield.Platform.Results;
using NetShield.Platform.Time;

namespace NetShield.Inventory.Collector.Handlers;

/// <summary>
/// Records that a collector is alive, and tells it how the API wants it paced.
/// </summary>
/// <remarks>
/// <para>
/// One row per collector, keyed by the name it calls itself, updated rather than appended — the
/// question this answers is "is anything collecting right now", and a table that grew by a row
/// every fifteen seconds would answer it slowly and cost a retention policy of its own.
/// </para>
/// <para>
/// The acknowledgement is where the API's ownership of scheduling becomes visible to the
/// collector: the poll interval, the lease duration and the batch ceiling come back on every
/// heartbeat, so a collector adopts the server's pacing rather than carrying its own copy of it
/// in a configuration file that can drift.
/// </para>
/// </remarks>
internal sealed class RecordHeartbeatHandler(
    InventoryDbContext context,
    IOptions<CollectorJobOptions> options,
    IClock clock)
{
    public async Task<Result<CollectorHeartbeatAck>> HandleAsync(
        CollectorHeartbeatRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string name = request.Name.Trim();
        string normalized = name.ToUpperInvariant();

        DateTimeOffset now = clock.UtcNow;

        CollectorNode? node = await context.CollectorNodes
            .SingleOrDefaultAsync(candidate => candidate.NormalizedName == normalized, cancellationToken);

        if (node is null)
        {
            node = new CollectorNode
            {
                Id = Guid.CreateVersion7(now),
                Name = name,
                NormalizedName = normalized,
                CreatedAt = now
            };

            context.CollectorNodes.Add(node);
        }

        // Every one of these is the collector's own claim about itself, taken at face value. The
        // shared secret says a collector is talking; it does not say which, so nothing here is
        // trusted for anything but the health page.
        node.Name = name;
        node.Version = string.IsNullOrWhiteSpace(request.Version) ? null : request.Version.Trim();
        node.Capacity = request.Capacity;
        node.Running = request.Running;
        node.LastSeenAt = now;
        node.UpdatedAt = now;

        await context.SaveChangesAsync(cancellationToken);

        CollectorJobOptions settings = options.Value;

        return new CollectorHeartbeatAck(
            now,
            settings.PollSeconds,
            settings.LeaseSeconds,
            settings.MaxJobsPerLease);
    }
}
