using System.Globalization;

using NetShield.Platform.Paging;
using NetShield.Platform.Results;

namespace NetShield.Inventory.Credentials.Handlers;

/// <summary>
/// The keyset position a credential profile page resumes from: the sort value of the last row,
/// and its id.
/// </summary>
/// <remarks>
/// The id is part of the position even when the sort field is unique among live rows, because a
/// soft-deleted profile releases its name and two rows can then hold it. Same construction as
/// <c>DeviceCursor</c>, and for the same reason: a keyset walks values rather than offsets, so a
/// row inserted ahead of the cursor cannot shift the page under a caller.
/// </remarks>
internal sealed record CredentialProfileCursor(string SortValue, Guid Id)
{
    /// <summary>Separates the two halves. A unit separator occurs in neither.</summary>
    private const char Separator = '\u001f';

    /// <summary>Round-trips a timestamp without losing a tick to the format.</summary>
    private const string TimestampFormat = "O";

    /// <summary>The plain keyset position of a row, for the field the page was sorted by.</summary>
    internal static string PositionOf(CredentialProfile profile, CredentialProfileSortField sort)
    {
        ArgumentNullException.ThrowIfNull(profile);

        string sortValue = sort switch
        {
            CredentialProfileSortField.Name => profile.Name,
            _ => profile.CreatedAt.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture)
        };

        return $"{sortValue}{Separator}{profile.Id:D}";
    }

    /// <summary>Reads a cursor a caller sent back.</summary>
    internal static Result<CredentialProfileCursor> Decode(string cursor)
    {
        Result<string> decoded = Cursor.Decode(cursor);

        if (!decoded.IsSuccess)
        {
            return Result<CredentialProfileCursor>.Failure(decoded.Error);
        }

        string[] parts = decoded.Value.Split(Separator);

        if (parts.Length != 2 || !Guid.TryParseExact(parts[1], "D", out Guid id))
        {
            return Error.Validation(
                Cursor.InvalidCursorCode,
                "The cursor is not a cursor this endpoint issued.");
        }

        return new CredentialProfileCursor(parts[0], id);
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
