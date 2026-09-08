using System.Globalization;

using NetShield.Platform.Paging;
using NetShield.Platform.Results;

namespace NetShield.Inventory.Discovery.Handlers;

/// <summary>
/// The keyset position an interface page resumes from: the device's own <c>ifIndex</c>.
/// </summary>
/// <remarks>
/// <see cref="DiscoveryCursor"/> is a timestamp and an id, because everything it pages is
/// ordered by when it happened. Interfaces are not: a port list read in <c>ifIndex</c> order is
/// the order the device presents them in, which is the order an operator expects to read them
/// in. The index is unique per device, so it is a whole position on its own.
/// </remarks>
internal sealed record DeviceInterfaceCursor(int IfIndex)
{
    /// <summary>
    /// The plain keyset position of a row. Handed to <c>ToCursorPage</c>, which encodes it — a
    /// position that arrived already encoded would be encoded twice and never decode.
    /// </summary>
    internal static string Compose(int ifIndex) =>
        ifIndex.ToString(CultureInfo.InvariantCulture);

    /// <summary>Reads a cursor a caller sent back.</summary>
    internal static Result<DeviceInterfaceCursor> Decode(string cursor)
    {
        Result<string> decoded = Cursor.Decode(cursor);

        if (!decoded.IsSuccess)
        {
            return Result<DeviceInterfaceCursor>.Failure(decoded.Error);
        }

        return int.TryParse(decoded.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)
            ? new DeviceInterfaceCursor(index)
            : Error.Validation(
                Cursor.InvalidCursorCode,
                "The cursor is not a cursor this endpoint issued.");
    }
}
