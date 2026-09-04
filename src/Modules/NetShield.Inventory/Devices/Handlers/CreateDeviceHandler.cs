using System.Net;

using Microsoft.EntityFrameworkCore;

using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory;
using NetShield.Contracts.Inventory.Events;

using NetShield.Inventory.Persistence;

using NetShield.Platform.Auditing;
using NetShield.Platform.Authorization;
using NetShield.Platform.Messaging;
using NetShield.Platform.Results;
using NetShield.Platform.Time;

namespace NetShield.Inventory.Devices.Handlers;

/// <summary>Adds a device by hand.</summary>
/// <remarks>
/// The device row and the <see cref="DeviceCreated"/> outbox row are written by one
/// <c>SaveChangesAsync</c> on one context, so either both land or neither does
/// (ARCHITECTURE.md §5).
/// </remarks>
internal sealed class CreateDeviceHandler(
    InventoryDbContext context,
    OutboxEnlistment outbox,
    IResourceGuard guard,
    IAuditContext audit,
    IClock clock)
{
    public async Task<Result<DeviceDetail>> HandleAsync(
        CreateDeviceRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result permitted = guard.Require(Permission.InventoryWrite, GetDeviceListHandler.ResourceType);

        if (!permitted.IsSuccess)
        {
            return Result<DeviceDetail>.Failure(permitted.Error);
        }

        // Parsed rather than trusted: the validator has already accepted it, and parsing here is
        // what turns "1.2.3.004" and "1.2.3.4" into one value before the unique index sees them.
        IPAddress address = IPAddress.Parse(request.PrimaryIpAddress);

        if (await IsAddressTakenAsync(address, cancellationToken))
        {
            return DeviceErrors.DuplicateAddress(address.ToString());
        }

        DateTimeOffset now = clock.UtcNow;

        Device device = new()
        {
            Id = Guid.CreateVersion7(now),
            Hostname = request.Hostname.Trim(),
            PrimaryIpAddress = address,
            Vendor = request.Vendor,
            Model = Clean(request.Model),
            OsVersion = Clean(request.OsVersion),
            SerialNumber = Clean(request.SerialNumber),
            Site = Clean(request.Site),
            Role = request.Role,
            Criticality = request.Criticality,
            Environment = request.Environment,
            Owner = Clean(request.Owner),
            Tags = DeviceTags.Normalize(request.Tags),
            Notes = Clean(request.Notes),

            // Not from the request, and there is no member on the request to take it from.
            // Reachability is observed, never asserted (WP-1.4).
            State = DeviceState.Unknown,
            CreatedAt = now,
            UpdatedAt = now
        };

        context.Devices.Add(device);
        outbox.Enlist(context, new DeviceCreated(device.Id, device.Hostname, device.PrimaryIpAddress.ToString()));

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException failure) when (IsDuplicateAddress(failure))
        {
            // The check above narrows the window; the index closes it. Two concurrent creates at
            // one address both pass the read and one loses here, which is the only place the
            // guarantee can actually be made.
            return DeviceErrors.DuplicateAddress(address.ToString());
        }

        audit.Target(GetDeviceListHandler.ResourceType, device.Id.ToString());
        audit.Snapshot(before: null, after: device.ToAuditSnapshot());

        return device.ToDetail();
    }

    private Task<bool> IsAddressTakenAsync(IPAddress address, CancellationToken cancellationToken) =>
        context.Devices.AnyAsync(
            device => device.DeletedAt == null && device.PrimaryIpAddress.Equals(address),
            cancellationToken);

    /// <summary>
    /// Whether the database refused the write for the one reason this handler answers with a
    /// 409. Any other constraint failure is a bug and stays an exception.
    /// </summary>
    internal static bool IsDuplicateAddress(DbUpdateException failure) =>
        failure.InnerException is Npgsql.PostgresException
        {
            SqlState: Npgsql.PostgresErrorCodes.UniqueViolation
        } violation
        && violation.ConstraintName == DeviceConfiguration.PrimaryIpIndexName;

    /// <summary>An optional string that arrived as whitespace is absent, not blank.</summary>
    internal static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
