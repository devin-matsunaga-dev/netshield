using NetShield.Contracts.Paging;

namespace NetShield.Platform.Paging;

/// <summary>
/// Turns the rows a keyset query returned into the page an endpoint hands back.
/// </summary>
public static class CursorPageExtensions
{
    /// <summary>
    /// Builds a page from rows fetched with <see cref="PageRequest.FetchLimit"/>. The extra row,
    /// if it arrived, is dropped and becomes the next cursor instead.
    /// </summary>
    /// <param name="rows">The query result, ordered, of at most <see cref="PageRequest.FetchLimit"/> rows.</param>
    /// <param name="request">The paging arguments the rows were fetched for.</param>
    /// <param name="positionOf">Produces the keyset position of a row, which becomes its cursor.</param>
    /// <param name="totalCount">The total matching rows, when the endpoint can count them cheaply.</param>
    public static CursorPage<T> ToCursorPage<T>(
        this IReadOnlyList<T> rows,
        PageRequest request,
        Func<T, string> positionOf,
        long? totalCount = null)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(positionOf);

        if (rows.Count <= request.Limit)
        {
            return new CursorPage<T>(rows, NextCursor: null, totalCount);
        }

        List<T> page = [.. rows.Take(request.Limit)];

        return new CursorPage<T>(page, Cursor.Encode(positionOf(page[^1])), totalCount);
    }
}
