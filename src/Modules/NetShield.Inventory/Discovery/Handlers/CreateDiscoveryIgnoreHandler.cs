using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory;

using NetShield.Inventory.Persistence;

using NetShield.Platform.Auditing;
using NetShield.Platform.Authorization;
using NetShield.Platform.Results;
using NetShield.Platform.Time;

namespace NetShield.Inventory.Discovery.Handlers;

/// <summary>
/// Adds an address or range to the permanent ignore list.
/// </summary>
/// <remarks>
/// <para>
/// Adding an entry also settles the candidates it covers. An operator who has just said "never
/// offer me anything in this block" should not then have to dismiss the eleven candidates
/// already on the review list from inside it — and leaving them would contradict the entry they
/// just wrote.
/// </para>
/// <para>
/// A promoted candidate is left alone. It is a device now, and ignoring the block it sits in
/// says something about discovery rather than about the inventory.
/// </para>
/// </remarks>
internal sealed class CreateDiscoveryIgnoreHandler(
    InventoryDbContext context,
    IResourceGuard guard,
    IAuditContext audit,
    IClock clock,
    ILogger<CreateDiscoveryIgnoreHandler> logger)
{
    public async Task<Result<DiscoveryIgnoreEntry>> HandleAsync(
        CreateDiscoveryIgnoreRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result permitted = guard.Require(
            Permission.InventoryWrite,
            GetDiscoveryIgnoreListHandler.ResourceType);

        if (!permitted.IsSuccess)
        {
            return Result<DiscoveryIgnoreEntry>.Failure(permitted.Error);
        }

        Result<AddressRange> parsed = AddressRange.Parse(request.Cidr);

        if (!parsed.IsSuccess)
        {
            return Result<DiscoveryIgnoreEntry>.Failure(parsed.Error);
        }

        AddressRange block = parsed.Value;
        string cidr = block.ToString();

        if (await context.DiscoveryIgnores.AnyAsync(row => row.Cidr == cidr, cancellationToken))
        {
            return DiscoveryErrors.IgnoreExists(cidr);
        }

        DateTimeOffset now = clock.UtcNow;

        DiscoveryIgnore ignore = new()
        {
            Id = Guid.CreateVersion7(now),
            Cidr = cidr,
            Reason = CreateDiscoverySeedHandler.Clean(request.Reason),
            CreatedAt = now,
            UpdatedAt = now
        };

        context.DiscoveryIgnores.Add(ignore);

        int settled = await SettleCoveredCandidatesAsync(block, now, cancellationToken);

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "{Cidr} was added to the discovery ignore list, settling {Count} candidates",
            cidr,
            settled);

        audit.Target(GetDiscoveryIgnoreListHandler.ResourceType, ignore.Id.ToString());
        audit.Snapshot(before: null, after: ignore.ToAuditSnapshot());

        return ignore.ToContract();
    }

    /// <summary>
    /// Marks every candidate awaiting review that falls inside the new block as ignored.
    /// </summary>
    /// <remarks>
    /// Matched in memory rather than in SQL, for the reason <see cref="IgnoreList"/> is: the
    /// test is address-in-block, which is PostgreSQL's containment operator and not something EF
    /// can express. Only the undecided candidates are read, which is the review list — bounded
    /// by what sweeps have found rather than by the estate.
    /// </remarks>
    private async Task<int> SettleCoveredCandidatesAsync(
        AddressRange block,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        List<DiscoveryCandidate> pending = await context.DiscoveryCandidates
            .Where(candidate => candidate.Status == DiscoveryCandidateStatus.New)
            .ToListAsync(cancellationToken);

        int settled = 0;

        foreach (DiscoveryCandidate candidate in pending.Where(row => block.Contains(row.Address)))
        {
            candidate.Status = DiscoveryCandidateStatus.Ignored;
            candidate.SettledAt = now;
            candidate.UpdatedAt = now;
            settled++;
        }

        return settled;
    }
}
