using System.Globalization;

using NetShield.Platform.Paging;
using NetShield.Platform.Results;

namespace NetShield.Inventory.Devices.Handlers;

/// <summary>
/// The keyset position a device page resumes from: the sort value of the last row, and its id.
/// </summary>
/// <remarks>
/// <para>
/// The id is always part of the position, including when the sort field looks unique, because
/// <c>hostname</c> is not unique and a page boundary falling between two devices of the same name
/// would otherwise repeat a row or skip one. Pagination is stable across inserts for the same
/// reason: a keyset walks values rather than offsets, so a row added ahead of the cursor cannot
/// shift the page under a caller.
/// </para>
/// <para>
/// This type composes and reads the position; <see cref="Cursor"/> owns the base64url that makes
/// it opaque on the wire. Keeping the two apart is deliberate —
/// <see cref="CursorPageExtensions.ToCursorPage{T}"/> encodes what
/// <see cref="PositionOf"/> returns, and a position that arrived already encoded would be
/// encoded twice and never decode.
/// </para>
/// </remarks>
internal sealed record DeviceCursor(string SortValue, Guid Id)
{
    /// <summary>Separates the two halves. A unit separator occurs in neither.</summary>
    private const char Separator = '\u001f';

    /// <summary>Round-trips a timestamp without losing a tick to the format.</summary>
    private const string TimestampFormat = "O";

    /// <summary>
    /// The plain keyset position of a row, for the field the page was sorted by. Handed to
    /// <c>ToCursorPage</c>, which is what encodes it.
    /// </summary>
    internal static string PositionOf(Device device, DeviceSortField sort)
    {
        ArgumentNullException.ThrowIfNull(device);

        string sortValue = sort switch
        {
            DeviceSortField.Hostname => device.Hostname,
            _ => device.CreatedAt.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture)
        };

        return Compose(sortValue, device.Id);
    }

    /// <summary>Composes a position from its two halves.</summary>
    internal static string Compose(string sortValue, Guid id) => $"{sortValue}{Separator}{id:D}";

    /// <summary>Reads a cursor a caller sent back.</summary>
    internal static Result<DeviceCursor> Decode(string cursor)
    {
        Result<string> decoded = Cursor.Decode(cursor);

        if (!decoded.IsSuccess)
        {
            return Result<DeviceCursor>.Failure(decoded.Error);
        }

        string[] parts = decoded.Value.Split(Separator);

        if (parts.Length != 2 || !Guid.TryParseExact(parts[1], "D", out Guid id))
        {
            return Error.Validation(
                Cursor.InvalidCursorCode,
                "The cursor is not a cursor this endpoint issued.");
        }

        return new DeviceCursor(parts[0], id);
    }

    /// <summary>Reads the timestamp half back, for a page ordered by creation.</summary>
    internal bool TryReadTimestamp(out DateTimeOffset value) =>
        DateTimeOffset.TryParseExact(
            SortValue,
            TimestampFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out value);
}
