using System.Net;

using Microsoft.EntityFrameworkCore;

using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory;
using NetShield.Contracts.Paging;

using NetShield.Inventory.Persistence;

using NetShield.Platform.Authorization;
using NetShield.Platform.Paging;
using NetShield.Platform.Results;

namespace NetShield.Inventory.Devices.Handlers;

/// <summary>
/// Serves one page of the device list: filtered, sorted, and paged by keyset
/// (CONVENTIONS.md §4).
/// </summary>
internal sealed class GetDeviceListHandler(InventoryDbContext context, IResourceGuard guard)
{
    /// <summary>What an audit row and a refusal call this kind of thing.</summary>
    internal const string ResourceType = "device";

    public async Task<Result<CursorPage<DeviceSummary>>> HandleAsync(
        DeviceListQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Result permitted = guard.Require(Permission.InventoryRead, ResourceType);

        if (!permitted.IsSuccess)
        {
            return Result<CursorPage<DeviceSummary>>.Failure(permitted.Error);
        }

        // Soft-deleted devices are gone as far as every read is concerned. The row survives so
        // that telemetry and audit rows naming the device still resolve (CONVENTIONS.md §3).
        IQueryable<Device> devices = context.Devices.AsNoTracking()
            .Where(device => device.DeletedAt == null);

        devices = Filter(devices, query);

        // Counted before the page is cut, so the total describes the filter rather than the
        // page. Cheap at the 500-device target scale in SPEC.md §1; if the estate ever makes it
        // expensive, CursorPage.TotalCount is optional by design and this is what drops.
        long totalCount = await devices.LongCountAsync(cancellationToken);

        Result<IQueryable<Device>> positioned = ApplyCursor(devices, query);

        if (!positioned.IsSuccess)
        {
            return Result<CursorPage<DeviceSummary>>.Failure(positioned.Error);
        }

        // One row more than asked for. Whether it arrives is what says a next page exists,
        // without a second query.
        List<Device> rows = await Order(positioned.Value, query)
            .Take(query.Page.FetchLimit)
            .ToListAsync(cancellationToken);

        // Paged as entities, because the cursor is the position of a row and only the entity
        // carries the fields it is built from; mapped to the contract immediately after, which is
        // the one place a Device becomes something that may leave the module.
        CursorPage<Device> page = rows.ToCursorPage(
            query.Page,
            device => DeviceCursor.PositionOf(device, query.Sort),
            totalCount);

        return new CursorPage<DeviceSummary>(
            [.. page.Items.Select(device => device.ToSummary())],
            page.NextCursor,
            page.TotalCount);
    }

    private static IQueryable<Device> Filter(IQueryable<Device> devices, DeviceListQuery query)
    {
        if (query.State is { } state)
        {
            devices = devices.Where(device => device.State == state);
        }

        if (query.Vendor is { } vendor)
        {
            devices = devices.Where(device => device.Vendor == vendor);
        }

        if (query.Role is { } role)
        {
            devices = devices.Where(device => device.Role == role);
        }

        if (query.Criticality is { } criticality)
        {
            devices = devices.Where(device => device.Criticality == criticality);
        }

        if (query.Environment is { } environment)
        {
            devices = devices.Where(device => device.Environment == environment);
        }

        if (!string.IsNullOrWhiteSpace(query.Site))
        {
            string site = query.Site.Trim();

            devices = devices.Where(device => device.Site != null && EF.Functions.ILike(device.Site, site));
        }

        if (!string.IsNullOrWhiteSpace(query.Tag))
        {
            // Normalised the same way a stored tag was, so a filter for "Core" finds "core".
            string tag = DeviceTags.Normalize([query.Tag]).SingleOrDefault() ?? query.Tag;

            devices = devices.Where(device => device.Tags.Contains(tag));
        }

        return ApplySearch(devices, query.Search);
    }

    /// <summary>
    /// One box in the table header. Text that parses as an address looks the address up exactly
    /// — searching for 10.0.0.1 should not also return 10.0.0.10 — and anything else is a
    /// hostname prefix, which is what the index on <c>hostname</c> can answer.
    /// </summary>
    private static IQueryable<Device> ApplySearch(IQueryable<Device> devices, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return devices;
        }

        string trimmed = search.Trim();

        if (IPAddress.TryParse(trimmed, out IPAddress? address))
        {
            return devices.Where(device => device.PrimaryIpAddress.Equals(address));
        }

        string prefix = Escape(trimmed) + "%";

        return devices.Where(device => EF.Functions.ILike(device.Hostname, prefix, "\\"));
    }

    /// <summary>Keeps a wildcard the caller typed from being one the database acts on.</summary>
    private static string Escape(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal);

    /// <summary>
    /// Resumes after the row the cursor names. The comparison mirrors the sort exactly — a
    /// keyset that reads the order differently from the ORDER BY silently skips rows.
    /// </summary>
    private static Result<IQueryable<Device>> ApplyCursor(IQueryable<Device> devices, DeviceListQuery query)
    {
        if (query.Page.Cursor is not { } cursor)
        {
            return Result<IQueryable<Device>>.Success(devices);
        }

        Result<DeviceCursor> position = DeviceCursor.Decode(cursor);

        if (!position.IsSuccess)
        {
            return Result<IQueryable<Device>>.Failure(position.Error);
        }

        DeviceCursor from = position.Value;

        if (query.Sort is DeviceSortField.Hostname)
        {
            return Result<IQueryable<Device>>.Success(query.Descending
                ? devices.Where(device =>
                    string.Compare(device.Hostname, from.SortValue) < 0
                    || (device.Hostname == from.SortValue && device.Id < from.Id))
                : devices.Where(device =>
                    string.Compare(device.Hostname, from.SortValue) > 0
                    || (device.Hostname == from.SortValue && device.Id > from.Id)));
        }

        if (!from.TryReadTimestamp(out DateTimeOffset createdAt))
        {
            return Error.Validation(
                Cursor.InvalidCursorCode,
                "The cursor is not a cursor this endpoint issued.");
        }

        return Result<IQueryable<Device>>.Success(query.Descending
            ? devices.Where(device =>
                device.CreatedAt < createdAt
                || (device.CreatedAt == createdAt && device.Id < from.Id))
            : devices.Where(device =>
                device.CreatedAt > createdAt
                || (device.CreatedAt == createdAt && device.Id > from.Id)));
    }

    private static IOrderedQueryable<Device> Order(IQueryable<Device> devices, DeviceListQuery query) =>
        (query.Sort, query.Descending) switch
        {
            (DeviceSortField.Hostname, false) => devices.OrderBy(d => d.Hostname).ThenBy(d => d.Id),
            (DeviceSortField.Hostname, true) => devices.OrderByDescending(d => d.Hostname).ThenByDescending(d => d.Id),
            (_, true) => devices.OrderByDescending(d => d.CreatedAt).ThenByDescending(d => d.Id),
            _ => devices.OrderBy(d => d.CreatedAt).ThenBy(d => d.Id)
        };
}
