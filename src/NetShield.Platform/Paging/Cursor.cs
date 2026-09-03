using System.Buffers.Text;
using System.Text;

using NetShield.Platform.Results;

namespace NetShield.Platform.Paging;

/// <summary>
/// Encodes and decodes the opaque cursor a list endpoint hands back. The payload is a keyset
/// position — the sort values of the last row of a page — and it is base64url encoded so that
/// it survives a query string untouched and so that callers do not read it and start depending
/// on its shape.
/// </summary>
/// <remarks>
/// This is encoding, not encryption. A cursor is not a capability: an endpoint still authorises
/// every request, so a caller who edits one gains nothing but a rejection.
/// </remarks>
public static class Cursor
{
    /// <summary>The code returned when a cursor cannot be read.</summary>
    public const string InvalidCursorCode = "paging.invalid-cursor";

    /// <summary>Encodes a keyset position for the wire.</summary>
    public static string Encode(string position) =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(position));

    /// <summary>
    /// Reads a cursor a caller sent back. A malformed cursor is a bad request, not an
    /// exception — it is the one part of the query string a client cannot compose by hand.
    /// </summary>
    public static Result<string> Decode(string cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return Error.Validation(InvalidCursorCode, "The cursor is empty.");
        }

        try
        {
            return Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor));
        }
        catch (FormatException)
        {
            return Error.Validation(InvalidCursorCode, "The cursor is not a cursor this endpoint issued.");
        }
        catch (DecoderFallbackException)
        {
            return Error.Validation(InvalidCursorCode, "The cursor is not a cursor this endpoint issued.");
        }
    }
}
