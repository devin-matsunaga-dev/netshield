namespace NetShield.Contracts.Paging;

/// <summary>
/// The shape every list endpoint returns (CONVENTIONS.md §4). Offset pagination is not used,
/// so there is no page number and no total-page count.
/// </summary>
/// <param name="Items">The rows in this page, in the query's sort order.</param>
/// <param name="NextCursor">
/// The opaque cursor that fetches the following page, or <see langword="null"/> when this page
/// is the last one. Callers pass it back verbatim and never parse it.
/// </param>
/// <param name="TotalCount">
/// The total number of matching rows, when the endpoint can count them cheaply. Optional by
/// design: a count over a hypertable is not free and most callers do not need one.
/// </param>
public sealed record CursorPage<T>(IReadOnlyList<T> Items, string? NextCursor, long? TotalCount = null);
