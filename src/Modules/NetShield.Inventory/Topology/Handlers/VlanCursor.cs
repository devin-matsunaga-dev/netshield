using System.Globalization;

using NetShield.Platform.Paging;
using NetShield.Platform.Results;

namespace NetShield.Inventory.Topology.Handlers;

/// <summary>
/// The keyset position a VLAN page resumes from: the VLAN id.
/// </summary>
/// <remarks>
/// The id itself rather than a row's primary key, because both VLAN reads are ordered by it and
/// both are stable under insertion — a VLAN discovered between two pages either sorts into a page
/// already served or into one still to come, and either way no row is skipped or repeated. It is
/// the one place in this module where the natural key really is the sort key; every other list
/// pages by a UUID v7 because its natural key is not ordered.
///
/// One cursor type serves the estate-wide list and the per-device list, because both are ordered
/// by VLAN id ascending and a cursor from one is a meaningful position in the other.
/// </remarks>
internal sealed record VlanCursor(int VlanId)
{
    /// <summary>
    /// The plain keyset position of a row. Handed to <c>ToCursorPage</c>, which encodes it — a
    /// position that arrived already encoded would be encoded twice and never decode.
    /// </summary>
    internal static string Compose(int vlanId) => vlanId.ToString(CultureInfo.InvariantCulture);

    /// <summary>Reads a cursor a caller sent back.</summary>
    internal static Result<VlanCursor> Decode(string cursor)
    {
        Result<string> decoded = Cursor.Decode(cursor);

        if (!decoded.IsSuccess)
        {
            return Result<VlanCursor>.Failure(decoded.Error);
        }

        return int.TryParse(decoded.Value, CultureInfo.InvariantCulture, out int vlanId)
            ? new VlanCursor(vlanId)
            : Error.Validation(
                Cursor.InvalidCursorCode,
                "The cursor is not a cursor this endpoint issued.");
    }
}
