using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using NetShield.Inventory.Persistence;

using NetShield.Platform.Results;
using NetShield.Platform.Time;

namespace NetShield.Inventory.Collector;

/// <inheritdoc cref="ICollectorJobQueue"/>
internal sealed class CollectorJobQueue(
    InventoryDbContext context,
    IOptions<CollectorJobOptions> options,
    IClock clock) : ICollectorJobQueue
{
    public async Task<Result<Guid>> EnqueueAsync(NewCollectorJob job, CancellationToken cancellationToken)
    {
        Result<Guid> staged = await EnlistAsync(context, job, cancellationToken);

        if (!staged.IsSuccess)
        {
            return staged;
        }

        await context.SaveChangesAsync(cancellationToken);

        return staged;
    }

    public async Task<Result<Guid>> EnlistAsync(
        InventoryDbContext target,
        NewCollectorJob job,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(job);

        string? parameters = job.Parameters?.GetRawText();

        if (parameters is { Length: > CollectorLimits.ParametersLength })
        {
            return Result<Guid>.Failure(CollectorErrors.ParametersTooLarge(CollectorLimits.ParametersLength));
        }

        // Checked here rather than by a foreign key, because both tables soft-delete: a row that
        // still exists is not the same as a device that is still live, and only a query knows
        // the difference (WP-1.1, WP-1.2).
        if (job.DeviceId is { } deviceId && !await DeviceIsLiveAsync(target, deviceId, cancellationToken))
        {
            return Result<Guid>.Failure(CollectorErrors.UnknownDevice(deviceId));
        }

        if (job.CredentialProfileId is { } profileId
            && !await CredentialProfileIsLiveAsync(target, profileId, cancellationToken))
        {
            return Result<Guid>.Failure(CollectorErrors.UnknownCredentialProfile(profileId));
        }

        DateTimeOffset now = clock.UtcNow;

        CollectorJob queued = new()
        {
            Id = Guid.CreateVersion7(now),
            Kind = job.Kind,
            Status = Contracts.Collector.CollectorJobStatus.Pending,
            DeviceId = job.DeviceId,
            CredentialProfileId = job.CredentialProfileId,
            Parameters = parameters,
            DueAt = job.DueAt ?? now,
            Attempts = 0,
            MaxAttempts = options.Value.MaxAttempts,
            CreatedAt = now,
            UpdatedAt = now
        };

        target.CollectorJobs.Add(queued);

        return queued.Id;
    }

    private static Task<bool> DeviceIsLiveAsync(
        InventoryDbContext target,
        Guid deviceId,
        CancellationToken cancellationToken) =>
        target.Devices.AnyAsync(
            device => device.Id == deviceId && device.DeletedAt == null,
            cancellationToken);

    private static Task<bool> CredentialProfileIsLiveAsync(
        InventoryDbContext target,
        Guid profileId,
        CancellationToken cancellationToken) =>
        target.CredentialProfiles.AnyAsync(
            profile => profile.Id == profileId && profile.DeletedAt == null,
            cancellationToken);
}
