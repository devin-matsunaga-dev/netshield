using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using NetShield.Contracts.Collector;
using NetShield.Contracts.Identity;
using NetShield.Contracts.Paging;

using NetShield.Inventory.Devices;
using NetShield.Inventory.Devices.Handlers;
using NetShield.Inventory.Persistence;

using NetShield.Platform.Authorization;
using NetShield.Platform.Paging;
using NetShield.Platform.Results;

namespace NetShield.Inventory.Collector.Handlers;

/// <summary>
/// Serves one page of a device's collector queue, newest first.
/// </summary>
/// <remarks>
/// <para>
/// The first read of <c>collector_jobs</c> outside <c>/internal/collector/*</c>. It exists
/// because "I pressed walk — did anything happen?" had no answer in the product: until WP-2.5 the
/// only way to see whether a collector had ever claimed a job was to open the database.
/// </para>
/// <para>
/// Gated on <see cref="Permission.InventoryRead"/> rather than on
/// <see cref="Permission.DiscoveryRun"/>. Reading what is queued for a device is reading about
/// the device; <em>starting</em> or <em>stopping</em> work is the privilege the run permission
/// names, and the cancel route is where that is required.
/// </para>
/// <para>
/// A device that has never been queued anything answers an empty page rather than a 404, on the
/// terms the interface list set: it exists and has no jobs, which is a different fact from the
/// device not being there.
/// </para>
/// </remarks>
internal sealed class GetDeviceJobListHandler(InventoryDbContext context, IResourceGuard guard)
{
    public async Task<Result<CursorPage<CollectorJobSummary>>> HandleAsync(
        Guid deviceId,
        CollectorJobStatus? status,
        PageRequest page,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);

        Result permitted = guard.Require(
            Permission.InventoryRead,
            GetDeviceListHandler.ResourceType,
            deviceId.ToString());

        if (!permitted.IsSuccess)
        {
            return Result<CursorPage<CollectorJobSummary>>.Failure(permitted.Error);
        }

        bool exists = await context.Devices.AsNoTracking()
            .AnyAsync(device => device.Id == deviceId && device.DeletedAt == null, cancellationToken);

        if (!exists)
        {
            return DeviceErrors.NotFound(deviceId);
        }

        IQueryable<CollectorJob> jobs = context.CollectorJobs.AsNoTracking()
            .Where(job => job.DeviceId == deviceId);

        if (status is { } wanted)
        {
            jobs = jobs.Where(job => job.Status == wanted);
        }

        long totalCount = await jobs.LongCountAsync(cancellationToken);

        if (page.Cursor is { } cursor)
        {
            Result<DeviceJobCursor> position = DeviceJobCursor.Decode(cursor);

            if (!position.IsSuccess)
            {
                return Result<CursorPage<CollectorJobSummary>>.Failure(position.Error);
            }

            Guid from = position.Value.Id;

            // Descending, so the next page is everything queued *before* the last row seen.
            // `<` on a uuid is what the adjacency list uses in the other direction, and Npgsql
            // translates it to the column comparison rather than pulling the rows back.
            jobs = jobs.Where(job => job.Id < from);
        }

        List<CollectorJob> rows = await jobs
            .OrderByDescending(job => job.Id)
            .Take(page.FetchLimit)
            .ToListAsync(cancellationToken);

        CursorPage<CollectorJob> result = rows.ToCursorPage(
            page,
            job => DeviceJobCursor.Compose(job.Id),
            totalCount);

        return new CursorPage<CollectorJobSummary>(
            [.. result.Items.Select(ToSummary)],
            result.NextCursor,
            result.TotalCount);
    }

    /// <summary>
    /// The row as a person reads it — without the credential it runs under, the lease token that
    /// would let a caller submit a result for it, or the result blob itself.
    /// </summary>
    internal static CollectorJobSummary ToSummary(CollectorJob job) => new(
        job.Id,
        job.Kind,
        ReadWalk(job.Parameters),
        job.Status,
        job.Attempts,
        job.MaxAttempts,
        job.DueAt,
        job.LeasedBy,
        job.LeasedUntil,
        job.CompletedAt,
        job.Detail,
        job.Status == CollectorJobStatus.Pending,
        job.CreatedAt);

    /// <summary>
    /// Which read a <c>Discover</c> job is, from the discriminator its parameters carry.
    /// </summary>
    /// <remarks>
    /// Read here in memory rather than projected in SQL, for two reasons. A page is at most 200
    /// rows, so it costs nothing; and <c>parameters</c> is opaque to this module by design —
    /// WP-1.3 defines no job kind's parameters and the walks are the owning packages' vocabulary,
    /// not the collector contract's. A malformed or absent value is no walk rather than an error:
    /// this is a label on a screen, and a queue that refused to render because one row's
    /// parameters were odd would be worse than one that renders it as a bare <c>Discover</c>.
    /// </remarks>
    private static string? ReadWalk(string? parameters)
    {
        if (string.IsNullOrEmpty(parameters))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(parameters);

            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("walk", out JsonElement walk)
                && walk.ValueKind == JsonValueKind.String
                    ? walk.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
