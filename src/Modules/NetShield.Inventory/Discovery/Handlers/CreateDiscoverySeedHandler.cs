using Microsoft.EntityFrameworkCore;

using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory;

using NetShield.Inventory.Persistence;

using NetShield.Platform.Auditing;
using NetShield.Platform.Authorization;
using NetShield.Platform.Results;
using NetShield.Platform.Time;

namespace NetShield.Inventory.Discovery.Handlers;

/// <summary>Creates a discovery seed.</summary>
/// <remarks>
/// <para>
/// Gated on <see cref="Permission.PoliciesWrite"/>, whose own definition names discovery
/// schedules: a seed decides what NetShield does on its own, which is a different privilege from
/// editing a device somebody already entered.
/// </para>
/// <para>
/// A seed that is enabled falls due immediately. A person who has just described their estate
/// expects the first sweep to be minutes away rather than an interval away, and the schedule's
/// per-pass ceiling is what keeps that from being a thundering herd.
/// </para>
/// </remarks>
internal sealed class CreateDiscoverySeedHandler(
    InventoryDbContext context,
    IResourceGuard guard,
    IAuditContext audit,
    IClock clock)
{
    public async Task<Result<DiscoverySeedDetail>> HandleAsync(
        CreateDiscoverySeedRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result permitted = guard.Require(
            Permission.PoliciesWrite,
            GetDiscoverySeedListHandler.ResourceType);

        if (!permitted.IsSuccess)
        {
            return Result<DiscoverySeedDetail>.Failure(permitted.Error);
        }

        string name = request.Name.Trim();

        if (await IsNameTakenAsync(context, name, null, cancellationToken))
        {
            return DiscoveryErrors.SeedNameTaken(name);
        }

        // Parsed again rather than trusted: the validator has already accepted these, and
        // parsing here is what turns "10.0.0.5/24" and "10.0.0.0/24" into one stored value.
        Result<SweepPlan> planned = SweepPlan.Create(request.Ranges, request.Exclusions);

        if (!planned.IsSuccess)
        {
            return Result<DiscoverySeedDetail>.Failure(planned.Error);
        }

        DateTimeOffset now = clock.UtcNow;

        DiscoverySeed seed = new()
        {
            Id = Guid.CreateVersion7(now),
            Name = name,
            Description = Clean(request.Description),
            Enabled = request.Enabled,
            Ranges = Normalise(planned.Value.Ranges),
            Exclusions = Normalise(planned.Value.Exclusions),
            IntervalMinutes = request.IntervalMinutes,
            NextRunAt = request.Enabled ? now : null,
            CreatedAt = now,
            UpdatedAt = now
        };

        context.DiscoverySeeds.Add(seed);

        await context.SaveChangesAsync(cancellationToken);

        audit.Target(GetDiscoverySeedListHandler.ResourceType, seed.Id.ToString());
        audit.Snapshot(before: null, after: seed.ToAuditSnapshot());

        return seed.ToDetail();
    }

    /// <summary>Whether another live seed already holds the name, ignoring case.</summary>
    /// <remarks>
    /// Compared through <c>lower()</c> rather than through <c>ILIKE</c>, because the caller
    /// supplies the name and <c>%</c> and <c>_</c> are wildcards in a <c>LIKE</c> pattern: a seed
    /// called "Core%" would otherwise be refused for colliding with every name beginning "Core".
    /// It is also exactly the expression the unique index is built on, so the check and the
    /// guarantee agree.
    /// </remarks>
    internal static Task<bool> IsNameTakenAsync(
        InventoryDbContext context,
        string name,
        Guid? excluding,
        CancellationToken cancellationToken)
    {
        string lowered = name.ToLowerInvariant();

        return context.DiscoverySeeds.AnyAsync(
            seed => seed.DeletedAt == null
                && seed.Id != excluding
#pragma warning disable CA1304, CA1311 // Translated to PostgreSQL's lower(); a culture would not be.
                && seed.Name.ToLower() == lowered,
#pragma warning restore CA1304, CA1311
            cancellationToken);
    }

    /// <summary>The parsed blocks as the normalised text that is stored.</summary>
    internal static IReadOnlyList<string> Normalise(IReadOnlyList<AddressRange> blocks) =>
        [.. blocks.Select(block => block.ToString())];

    /// <summary>An optional string that arrived as whitespace is absent, not blank.</summary>
    internal static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
