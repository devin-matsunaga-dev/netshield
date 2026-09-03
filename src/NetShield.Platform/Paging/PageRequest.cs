using NetShield.Platform.Results;

namespace NetShield.Platform.Paging;

/// <summary>
/// A validated <c>?cursor=&amp;limit=</c> pair. CONVENTIONS.md §4: every list endpoint is cursor
/// paginated, the default page is 50 rows and no page exceeds 200.
/// </summary>
public sealed record PageRequest
{
    /// <summary>The page size used when the caller does not ask for one.</summary>
    public const int DefaultLimit = 50;

    /// <summary>The largest page any endpoint will serve.</summary>
    public const int MaxLimit = 200;

    /// <summary>The code returned when the requested limit is outside the permitted range.</summary>
    public const string InvalidLimitCode = "paging.invalid-limit";

    private PageRequest(int limit, string? cursor)
    {
        Limit = limit;
        Cursor = cursor;
    }

    /// <summary>How many rows the caller wants, between 1 and <see cref="MaxLimit"/>.</summary>
    public int Limit { get; }

    /// <summary>The cursor the caller was given by the previous page, if this is not the first.</summary>
    public string? Cursor { get; }

    /// <summary>
    /// How many rows the query should actually fetch: one more than asked for. The extra row is
    /// never returned; its existence is what tells the endpoint whether to issue a next cursor,
    /// without a second count query.
    /// </summary>
    public int FetchLimit => Limit + 1;

    /// <summary>
    /// Validates a caller's paging arguments.
    /// </summary>
    /// <remarks>
    /// A limit above <see cref="MaxLimit"/> is rejected rather than clamped. Silently serving
    /// 200 rows to a caller who asked for 5,000 reads to them as "there were only 200", which
    /// is a data-loss bug wearing a pagination costume.
    /// </remarks>
    public static Result<PageRequest> Create(string? cursor, int? limit)
    {
        if (limit is { } requested && (requested < 1 || requested > MaxLimit))
        {
            return Error.Validation(
                InvalidLimitCode,
                $"limit must be between 1 and {MaxLimit}.",
                new Dictionary<string, string[]>
                {
                    ["limit"] = [$"Must be between 1 and {MaxLimit}. The default is {DefaultLimit}."]
                });
        }

        return new PageRequest(limit ?? DefaultLimit, string.IsNullOrWhiteSpace(cursor) ? null : cursor);
    }
}
