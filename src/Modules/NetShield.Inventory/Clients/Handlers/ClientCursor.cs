using System.Globalization;

using NetShield.Platform.Paging;
using NetShield.Platform.Results;

namespace NetShield.Inventory.Clients.Handlers;

/// <summary>
/// The keyset position a client page resumes from: when the row was last seen, and its id.
/// </summary>
/// <remarks>
/// <para>
/// The list is ordered most-recently-seen first, because the question a client list answers is
/// "what is on the network" rather than "what has ever been". The id is part of the position and
/// has to be: a single walk stamps every client it confirmed with the same instant, so a page
/// boundary falling inside one walk's worth of rows would otherwise repeat a row or skip one.
/// </para>
/// <para>
/// This type composes and reads the position; <see cref="Cursor"/> owns the base64url that makes
/// it opaque on the wire — a position that arrived already encoded would be encoded twice and
/// never decode.
/// </para>
/// </remarks>
internal sealed record ClientCursor(DateTimeOffset LastSeenAt, Guid Id)
{
    /// <summary>Separates the two halves. A unit separator occurs in neither.</summary>
    private const char Separator = '\u001f';

    /// <summary>Round-trips a timestamp without losing a tick to the format.</summary>
    private const string TimestampFormat = "O";

    /// <summary>The plain keyset position of a row. Handed to <c>ToCursorPage</c>, which encodes it.</summary>
    internal static string Compose(DateTimeOffset lastSeenAt, Guid id) =>
        $"{lastSeenAt.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture)}{Separator}{id:D}";

    /// <summary>Reads a cursor a caller sent back.</summary>
    internal static Result<ClientCursor> Decode(string cursor)
    {
        Result<string> decoded = Cursor.Decode(cursor);

        if (!decoded.IsSuccess)
        {
            return Result<ClientCursor>.Failure(decoded.Error);
        }

        string[] parts = decoded.Value.Split(Separator);

        if (parts.Length != 2
            || !Guid.TryParseExact(parts[1], "D", out Guid id)
            || !DateTimeOffset.TryParseExact(
                parts[0],
                TimestampFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset lastSeenAt))
        {
            return Error.Validation(
                Cursor.InvalidCursorCode,
                "The cursor is not a cursor this endpoint issued.");
        }

        return new ClientCursor(lastSeenAt, id);
    }
}
