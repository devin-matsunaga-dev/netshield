using System.Globalization;

using NetShield.Platform.Paging;
using NetShield.Platform.Results;

namespace NetShield.Inventory.Clients.Handlers;

/// <summary>
/// The keyset position a binding-history page resumes from: when the interval opened, and its id.
/// </summary>
/// <remarks>
/// A history reads newest first, which is the order somebody investigating an attribution walks
/// it in — "what held this address, and what before that". The id is part of the position
/// because one walk can open several intervals at one instant, so the timestamp alone is not a
/// position.
///
/// One cursor type serves both histories. They are the same shape and the same order, and two
/// copies of this arithmetic would be two places for it to drift.
/// </remarks>
internal sealed record ClientBindingCursor(DateTimeOffset ObservedFrom, Guid Id)
{
    /// <summary>Separates the two halves. A unit separator occurs in neither.</summary>
    private const char Separator = '\u001f';

    /// <summary>Round-trips a timestamp without losing a tick to the format.</summary>
    private const string TimestampFormat = "O";

    /// <summary>The plain keyset position of a row. Handed to <c>ToCursorPage</c>, which encodes it.</summary>
    internal static string Compose(DateTimeOffset observedFrom, Guid id) =>
        $"{observedFrom.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture)}{Separator}{id:D}";

    /// <summary>Reads a cursor a caller sent back.</summary>
    internal static Result<ClientBindingCursor> Decode(string cursor)
    {
        Result<string> decoded = Cursor.Decode(cursor);

        if (!decoded.IsSuccess)
        {
            return Result<ClientBindingCursor>.Failure(decoded.Error);
        }

        string[] parts = decoded.Value.Split(Separator);

        if (parts.Length != 2
            || !Guid.TryParseExact(parts[1], "D", out Guid id)
            || !DateTimeOffset.TryParseExact(
                parts[0],
                TimestampFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset observedFrom))
        {
            return Error.Validation(
                Cursor.InvalidCursorCode,
                "The cursor is not a cursor this endpoint issued.");
        }

        return new ClientBindingCursor(observedFrom, id);
    }
}
