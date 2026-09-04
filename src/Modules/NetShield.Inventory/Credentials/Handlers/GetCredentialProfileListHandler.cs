using Microsoft.EntityFrameworkCore;

using NetShield.Contracts.Identity;
using NetShield.Contracts.Inventory;
using NetShield.Contracts.Paging;

using NetShield.Inventory.Persistence;

using NetShield.Platform.Authorization;
using NetShield.Platform.Paging;
using NetShield.Platform.Results;

namespace NetShield.Inventory.Credentials.Handlers;

/// <summary>
/// Serves one page of the credential profile list: filtered, sorted, and paged by keyset
/// (CONVENTIONS.md §4).
/// </summary>
/// <remarks>
/// Reading is gated on <see cref="Permission.CredentialsManage"/> rather than on
/// <c>InventoryRead</c>. There is no <c>CredentialsRead</c> member and WP-1.2 was not told to add
/// one; meanwhile a profile's username is itself half of an SSH credential, and the set of names
/// tells a reader exactly which accounts NetShield holds passwords for. Over-restricting is the
/// recoverable mistake of the two.
/// </remarks>
internal sealed class GetCredentialProfileListHandler(InventoryDbContext context, IResourceGuard guard)
{
    public async Task<Result<CursorPage<CredentialProfileSummary>>> HandleAsync(
        CredentialProfileListQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Result permitted = guard.Require(Permission.CredentialsManage, CredentialResolver.ResourceType);

        if (!permitted.IsSuccess)
        {
            return Result<CursorPage<CredentialProfileSummary>>.Failure(permitted.Error);
        }

        IQueryable<CredentialProfile> profiles = context.CredentialProfiles.AsNoTracking()
            .Where(profile => profile.DeletedAt == null);

        profiles = Filter(profiles, query);

        long totalCount = await profiles.LongCountAsync(cancellationToken);

        Result<IQueryable<CredentialProfile>> positioned = ApplyCursor(profiles, query);

        if (!positioned.IsSuccess)
        {
            return Result<CursorPage<CredentialProfileSummary>>.Failure(positioned.Error);
        }

        List<CredentialProfile> rows = await Order(positioned.Value, query)
            .Take(query.Page.FetchLimit)
            .ToListAsync(cancellationToken);

        CursorPage<CredentialProfile> page = rows.ToCursorPage(
            query.Page,
            profile => CredentialProfileCursor.PositionOf(profile, query.Sort),
            totalCount);

        // One grouped query for the whole page rather than a count per row: the list is the one
        // place the N+1 would actually show at the 500-device scale in SPEC.md §1.
        IReadOnlyDictionary<Guid, int> counts = await CountDevicesAsync(
            [.. page.Items.Select(profile => profile.Id)],
            context,
            cancellationToken);

        return new CursorPage<CredentialProfileSummary>(
            [.. page.Items.Select(profile => profile.ToSummary(counts.GetValueOrDefault(profile.Id)))],
            page.NextCursor,
            page.TotalCount);
    }

    /// <summary>
    /// How many live devices each of these profiles is assigned to. A profile assigned only to
    /// devices that have since been removed counts zero — the assignment row survives the soft
    /// delete, and reporting it would say the credential is in use when nothing will use it.
    /// </summary>
    internal static async Task<IReadOnlyDictionary<Guid, int>> CountDevicesAsync(
        IReadOnlyList<Guid> profileIds,
        InventoryDbContext context,
        CancellationToken cancellationToken)
    {
        if (profileIds.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        return await context.DeviceCredentialProfiles.AsNoTracking()
            .Where(assignment => profileIds.Contains(assignment.CredentialProfileId))
            .Where(assignment => context.Devices
                .Any(device => device.Id == assignment.DeviceId && device.DeletedAt == null))
            .GroupBy(assignment => assignment.CredentialProfileId)
            .ToDictionaryAsync(group => group.Key, group => group.Count(), cancellationToken);
    }

    private static IQueryable<CredentialProfile> Filter(
        IQueryable<CredentialProfile> profiles,
        CredentialProfileListQuery query)
    {
        if (query.Kind is { } kind)
        {
            profiles = profiles.Where(profile => profile.Kind == kind);
        }

        if (string.IsNullOrWhiteSpace(query.Search))
        {
            return profiles;
        }

        // Matched against the folded column, which is what the unique index is over, so the
        // search and the uniqueness rule agree about what "the same name" means.
        string prefix = Escape(CredentialLimits.NormalizeName(query.Search)) + "%";

        return profiles.Where(profile => EF.Functions.Like(profile.NormalizedName, prefix, "\\"));
    }

    /// <summary>Keeps a wildcard the caller typed from being one the database acts on.</summary>
    private static string Escape(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal);

    /// <summary>
    /// Resumes after the row the cursor names. The comparison mirrors the sort exactly — a keyset
    /// that reads the order differently from the ORDER BY silently skips rows.
    /// </summary>
    private static Result<IQueryable<CredentialProfile>> ApplyCursor(
        IQueryable<CredentialProfile> profiles,
        CredentialProfileListQuery query)
    {
        if (query.Page.Cursor is not { } cursor)
        {
            return Result<IQueryable<CredentialProfile>>.Success(profiles);
        }

        Result<CredentialProfileCursor> position = CredentialProfileCursor.Decode(cursor);

        if (!position.IsSuccess)
        {
            return Result<IQueryable<CredentialProfile>>.Failure(position.Error);
        }

        CredentialProfileCursor from = position.Value;

        if (query.Sort is CredentialProfileSortField.Name)
        {
            return Result<IQueryable<CredentialProfile>>.Success(query.Descending
                ? profiles.Where(profile =>
                    string.Compare(profile.Name, from.SortValue) < 0
                    || (profile.Name == from.SortValue && profile.Id < from.Id))
                : profiles.Where(profile =>
                    string.Compare(profile.Name, from.SortValue) > 0
                    || (profile.Name == from.SortValue && profile.Id > from.Id)));
        }

        if (!from.TryReadTimestamp(out DateTimeOffset createdAt))
        {
            return Error.Validation(
                Cursor.InvalidCursorCode,
                "The cursor is not a cursor this endpoint issued.");
        }

        return Result<IQueryable<CredentialProfile>>.Success(query.Descending
            ? profiles.Where(profile =>
                profile.CreatedAt < createdAt
                || (profile.CreatedAt == createdAt && profile.Id < from.Id))
            : profiles.Where(profile =>
                profile.CreatedAt > createdAt
                || (profile.CreatedAt == createdAt && profile.Id > from.Id)));
    }

    private static IOrderedQueryable<CredentialProfile> Order(
        IQueryable<CredentialProfile> profiles,
        CredentialProfileListQuery query) =>
        (query.Sort, query.Descending) switch
        {
            (CredentialProfileSortField.Name, false) => profiles.OrderBy(p => p.Name).ThenBy(p => p.Id),
            (CredentialProfileSortField.Name, true) =>
                profiles.OrderByDescending(p => p.Name).ThenByDescending(p => p.Id),
            (_, true) => profiles.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id),
            _ => profiles.OrderBy(p => p.CreatedAt).ThenBy(p => p.Id)
        };
}
