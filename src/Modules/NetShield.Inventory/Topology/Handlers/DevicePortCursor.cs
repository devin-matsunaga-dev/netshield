using System.Globalization;

using NetShield.Platform.Paging;
using NetShield.Platform.Results;

namespace NetShield.Inventory.Topology.Handlers;

/// <summary>
/// The keyset position a port page resumes from: the device's own <c>ifIndex</c>.
/// </summary>
/// <remarks>
/// The same position <c>DeviceInterfaceCursor</c> uses, and a separate type rather than a shared
/// one because the two page different sets — the interface list is what a walk recorded, and this
/// is that unioned with every port an observation names. A cursor is part of a route's contract,
/// and two routes sharing a cursor type is how one of them later changes its ordering and
/// silently breaks the other's resumption.
/// </remarks>
internal sealed record DevicePortCursor(int IfIndex)
{
    /// <summary>
    /// The plain keyset position of a row. Handed to <c>ToCursorPage</c>, which encodes it — a
    /// position that arrived already encoded would be encoded twice and never decode.
    /// </summary>
    internal static string Compose(int ifIndex) =>
        ifIndex.ToString(CultureInfo.InvariantCulture);

    /// <summary>Reads a cursor a caller sent back.</summary>
    internal static Result<DevicePortCursor> Decode(string cursor)
    {
        Result<string> decoded = Cursor.Decode(cursor);

        if (!decoded.IsSuccess)
        {
            return Result<DevicePortCursor>.Failure(decoded.Error);
        }

        return int.TryParse(decoded.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)
            ? new DevicePortCursor(index)
            : Error.Validation(
                Cursor.InvalidCursorCode,
                "The cursor is not a cursor this endpoint issued.");
    }
}
