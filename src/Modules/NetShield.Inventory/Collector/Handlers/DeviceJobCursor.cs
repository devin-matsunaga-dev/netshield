using System.Globalization;

using NetShield.Platform.Paging;
using NetShield.Platform.Results;

namespace NetShield.Inventory.Collector.Handlers;

/// <summary>
/// The keyset position a device's job page resumes from: the job's own id.
/// </summary>
/// <remarks>
/// A UUID v7, so ordering by it descending is ordering by when the job was queued, newest first
/// — which is the order the question is asked in. Somebody opening this screen has just pressed
/// a button and wants to know what happened to <em>that</em> job; the walk from last Tuesday is
/// what they scroll to. One column, and it needs no tie-break because it is the primary key.
/// </remarks>
internal sealed record DeviceJobCursor(Guid Id)
{
    /// <summary>
    /// The plain keyset position of a row. Handed to <c>ToCursorPage</c>, which encodes it — a
    /// position that arrived already encoded would be encoded twice and never decode.
    /// </summary>
    internal static string Compose(Guid id) => id.ToString("D", CultureInfo.InvariantCulture);

    /// <summary>Reads a cursor a caller sent back.</summary>
    internal static Result<DeviceJobCursor> Decode(string cursor)
    {
        Result<string> decoded = Cursor.Decode(cursor);

        if (!decoded.IsSuccess)
        {
            return Result<DeviceJobCursor>.Failure(decoded.Error);
        }

        return Guid.TryParse(decoded.Value, CultureInfo.InvariantCulture, out Guid id)
            ? new DeviceJobCursor(id)
            : Error.Validation(
                Cursor.InvalidCursorCode,
                "The cursor is not a cursor this endpoint issued.");
    }
}
