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
/// Takes a candidate off the review list for good, and puts its address on the ignore list.
/// </summary>
/// <remarks>
/// <para>
/// Two things at once, deliberately. Marking the candidate is what clears the review list;
/// adding the ignore entry is what makes the WP-1.6 criterion hold — the next sweep sees the
/// address answer, finds it ignored, and records that outcome without creating a candidate
/// again.
/// </para>
/// <para>
/// It ignores the single address rather than the block around it. Somebody dismissing one
/// printer has not said anything about the rest of its subnet, and the range form of the same
/// decision is <c>POST /api/v1/discovery/ignores</c>.
/// </para>
/// </remarks>
internal sealed class IgnoreDiscoveryCandidateHandler(
    InventoryDbContext context,
    IResourceGuard guard,
    IAuditContext audit,
    IClock clock,
    ILogger<IgnoreDiscoveryCandidateHandler> logger)
{
    public async Task<Result<DiscoveryIgnoreEntry>> HandleAsync(
        Guid candidateId,
        CancellationToken cancellationToken)
    {
        Result permitted = guard.Require(
            Permission.InventoryWrite,
            GetDiscoveryCandidateListHandler.ResourceType,
            candidateId.ToString());

        if (!permitted.IsSuccess)
        {
            return Result<DiscoveryIgnoreEntry>.Failure(permitted.Error);
        }

        DiscoveryCandidate? candidate = await context.DiscoveryCandidates
            .SingleOrDefaultAsync(row => row.Id == candidateId, cancellationToken);

        if (candidate is null)
        {
            return DiscoveryErrors.CandidateNotFound(candidateId);
        }

        if (candidate.Status != DiscoveryCandidateStatus.New)
        {
            return DiscoveryErrors.CandidateSettled(candidateId);
        }

        Result<AddressRange> block = AddressRange.Parse(candidate.Address.ToString());

        if (!block.IsSuccess)
        {
            return Result<DiscoveryIgnoreEntry>.Failure(block.Error);
        }

        string cidr = block.Value.ToString();
        DateTimeOffset now = clock.UtcNow;

        // An entry may already exist — the address could sit inside a range somebody ignored
        // earlier without the candidate having been settled. Reusing it keeps the list free of
        // two entries that say the same thing.
        DiscoveryIgnore? ignore = await context.DiscoveryIgnores
            .SingleOrDefaultAsync(row => row.Cidr == cidr, cancellationToken);

        if (ignore is null)
        {
            ignore = new DiscoveryIgnore
            {
                Id = Guid.CreateVersion7(now),
                Cidr = cidr,
                Reason = "Dismissed from the discovery review list.",
                CreatedAt = now,
                UpdatedAt = now
            };

            context.DiscoveryIgnores.Add(ignore);
        }

        candidate.Status = DiscoveryCandidateStatus.Ignored;
        candidate.SettledAt = now;
        candidate.UpdatedAt = now;

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Discovery candidate {CandidateId} was ignored, and {Cidr} was added to the ignore list",
            candidateId,
            cidr);

        audit.Target(GetDiscoveryCandidateListHandler.ResourceType, candidateId.ToString());
        audit.Snapshot(before: null, after: ignore.ToAuditSnapshot());

        return ignore.ToContract();
    }
}
