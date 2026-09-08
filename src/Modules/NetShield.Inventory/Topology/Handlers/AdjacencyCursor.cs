using System.Globalization;

using NetShield.Platform.Paging;
using NetShield.Platform.Results;

namespace NetShield.Inventory.Topology.Handlers;

/// <summary>
/// The keyset position an adjacency page resumes from: the edge's own id.
/// </summary>
/// <remarks>
/// A UUID v7, so ordering by it is ordering by when the edge was first discovered — which is
/// also the order a reader wants, oldest link first, and needs no second column to break ties.
/// The interface index would have been the natural key, as it is for the interface list, except
/// that a device's edges are read from both ends of the table and the two ends have two
/// different indexes.
/// </remarks>
internal sealed record AdjacencyCursor(Guid Id)
{
    /// <summary>
    /// The plain keyset position of a row. Handed to <c>ToCursorPage</c>, which encodes it — a
    /// position that arrived already encoded would be encoded twice and never decode.
    /// </summary>
    internal static string Compose(Guid id) => id.ToString("D", CultureInfo.InvariantCulture);

    /// <summary>Reads a cursor a caller sent back.</summary>
    internal static Result<AdjacencyCursor> Decode(string cursor)
    {
        Result<string> decoded = Cursor.Decode(cursor);

        if (!decoded.IsSuccess)
        {
            return Result<AdjacencyCursor>.Failure(decoded.Error);
        }

        return Guid.TryParse(decoded.Value, CultureInfo.InvariantCulture, out Guid id)
            ? new AdjacencyCursor(id)
            : Error.Validation(
                Cursor.InvalidCursorCode,
                "The cursor is not a cursor this endpoint issued.");
    }
}
