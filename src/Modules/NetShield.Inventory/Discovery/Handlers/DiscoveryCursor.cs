using System.Globalization;

using NetShield.Platform.Paging;
using NetShield.Platform.Results;

namespace NetShield.Inventory.Discovery.Handlers;

/// <summary>
/// The keyset position a discovery page resumes from: a timestamp and the row's id.
/// </summary>
/// <remarks>
/// <para>
/// One cursor for four lists — seeds, runs, run hosts and candidates — because all four are
/// ordered by a timestamp and then by id, and four near-identical cursor types would be four
/// places for the same off-by-one to hide. <c>DeviceCursor</c> stays separate because the device
/// list can also be sorted by hostname, and a cursor that can hold either has to say which.
/// </para>
/// <para>
/// The id is always part of the position, including where the timestamp looks unique: two runs
/// started in the same tick, or two candidates seen by the same sweep, share one — and a page
/// boundary falling between them would otherwise repeat a row or skip one.
/// </para>
/// </remarks>
internal sealed record DiscoveryCursor(DateTimeOffset Timestamp, Guid Id)
{
    /// <summary>Separates the two halves. A unit separator occurs in neither.</summary>
    private const char Separator = '\u001f';

    /// <summary>Round-trips a timestamp without losing a tick to the format.</summary>
    private const string TimestampFormat = "O";

    /// <summary>
    /// The plain keyset position of a row. Handed to <c>ToCursorPage</c>, which is what encodes
    /// it — a position that arrived already encoded would be encoded twice and never decode.
    /// </summary>
    internal static string Compose(DateTimeOffset timestamp, Guid id) =>
        $"{timestamp.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture)}{Separator}{id:D}";

    /// <summary>Reads a cursor a caller sent back.</summary>
    internal static Result<DiscoveryCursor> Decode(string cursor)
    {
        Result<string> decoded = Cursor.Decode(cursor);

        if (!decoded.IsSuccess)
        {
            return Result<DiscoveryCursor>.Failure(decoded.Error);
        }

        string[] parts = decoded.Value.Split(Separator);

        if (parts.Length != 2
            || !Guid.TryParseExact(parts[1], "D", out Guid id)
            || !DateTimeOffset.TryParseExact(
                parts[0],
                TimestampFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset timestamp))
        {
            return Error.Validation(
                Cursor.InvalidCursorCode,
                "The cursor is not a cursor this endpoint issued.");
        }

        return new DiscoveryCursor(timestamp, id);
    }
}
