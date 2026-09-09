using System.Globalization;

using NetShield.Platform.Paging;
using NetShield.Platform.Results;

namespace NetShield.Inventory.Topology.Handlers;

/// <summary>
/// The keyset position a graph page resumes from: the component, the rank within it, and the
/// device.
/// </summary>
/// <remarks>
/// <para>
/// This is what "paginated by subgraph" comes to. A graph has no natural row order, so the
/// ordering is the one the layout produces — component first, largest island before smallest,
/// then distance from that island's root, then the device id to break the tie. A page is a
/// contiguous slice of it, which is why page one is the estate's core outwards and the islands
/// arrive last.
/// </para>
/// <para>
/// <strong>The drawing order within a rank is deliberately not in the key.</strong> A barycentre
/// position moves when a node elsewhere in the graph does, and a cursor has to name a position
/// that will still be where it was on the next request. A device id does not move.
/// </para>
/// </remarks>
/// <param name="ComponentIndex">The island the last node of the previous page was on.</param>
/// <param name="Rank">Its distance from that island's root.</param>
/// <param name="DeviceId">The device itself.</param>
internal sealed record TopologyGraphCursor(int ComponentIndex, int Rank, Guid DeviceId)
{
    /// <summary>
    /// The plain keyset position of a node. Handed to <c>Cursor.Encode</c> by the handler — a
    /// position that arrived already encoded would be encoded twice and never decode.
    /// </summary>
    internal static string Compose(int componentIndex, int rank, Guid deviceId) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{componentIndex}:{rank}:{deviceId:D}");

    /// <summary>Reads a cursor a caller sent back.</summary>
    internal static Result<TopologyGraphCursor> Decode(string cursor)
    {
        Result<string> decoded = Cursor.Decode(cursor);

        if (!decoded.IsSuccess)
        {
            return Result<TopologyGraphCursor>.Failure(decoded.Error);
        }

        string[] parts = decoded.Value.Split(':');

        if (parts.Length == 3
            && int.TryParse(parts[0], CultureInfo.InvariantCulture, out int componentIndex)
            && componentIndex >= 0
            && int.TryParse(parts[1], CultureInfo.InvariantCulture, out int rank)
            && rank >= 0
            && Guid.TryParse(parts[2], CultureInfo.InvariantCulture, out Guid deviceId))
        {
            return new TopologyGraphCursor(componentIndex, rank, deviceId);
        }

        return Error.Validation(
            Cursor.InvalidCursorCode,
            "The cursor is not a cursor this endpoint issued.");
    }

    /// <summary>Whether a node falls after this position in the graph's node ordering.</summary>
    internal bool Precedes(int componentIndex, int rank, Guid deviceId)
    {
        if (componentIndex != ComponentIndex)
        {
            return componentIndex > ComponentIndex;
        }

        return rank != Rank ? rank > Rank : deviceId > DeviceId;
    }
}
