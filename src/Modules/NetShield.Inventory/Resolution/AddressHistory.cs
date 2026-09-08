using System.Text.Json.Serialization;

using NetShield.Contracts.Inventory;

namespace NetShield.Inventory.Resolution;

/// <summary>
/// Everything needed to resolve one address at any instant, as it is cached.
/// </summary>
/// <remarks>
/// <para>
/// A whole address's recent history rather than one answer, because the instant is what varies
/// and the history is what does not. One cache read then answers every timestamp inside the
/// window, which is what makes a resolution warm at all — an entry per (address, instant) would
/// have an unbounded key space and nothing to invalidate.
/// </para>
/// <para>
/// <strong>It is bounded, and it says so.</strong> Only the most recent
/// <c>ClientLimits.CachedBindings</c> intervals are carried. <see cref="Horizon"/> is the
/// <c>ObservedFrom</c> of the oldest one when more may exist before it, and null when the whole
/// history fits — so the resolver can tell "this address had no binding then" from "I did not
/// look that far back" and read through to the database for the second. Without that
/// distinction, a DHCP pool address that has changed hands often would start answering
/// <c>Unresolved</c> for anything older than the window.
/// </para>
/// </remarks>
/// <param name="Device">The live device whose primary address this is, if one has it.</param>
/// <param name="Bindings">
/// The client intervals for this address, newest first, bounded. Never overlapping: the table
/// they came from has a partial unique index making one open interval per address impossible to
/// violate, and a closed interval ends exactly where its successor begins.
/// </param>
/// <param name="Horizon">
/// The earliest instant this document can answer for, or <see langword="null"/> when it holds the
/// address's whole history. A query before it falls through to the database.
/// </param>
internal sealed record AddressHistory(
    [property: JsonPropertyName("device")] CachedDevice? Device,
    [property: JsonPropertyName("bindings")] IReadOnlyList<CachedBinding> Bindings,
    [property: JsonPropertyName("horizon")] DateTimeOffset? Horizon)
{
    /// <summary>Whether this document can answer for <paramref name="at"/>.</summary>
    /// <remarks>
    /// A miss is not a wrong answer here — it is the resolver deciding to ask the database
    /// instead, which is the same thing a cold cache makes it do.
    /// </remarks>
    internal bool Covers(DateTimeOffset at) => Horizon is not { } horizon || at >= horizon;

    /// <summary>The interval covering <paramref name="at"/>, or nothing.</summary>
    /// <remarks>
    /// Half-open: <c>ObservedFrom</c> is inside the interval and <c>ObservedTo</c> is not. That
    /// is what makes a boundary instant belong to exactly one of the two intervals meeting at it,
    /// which is the whole point of closing a binding where its successor opens.
    /// </remarks>
    internal CachedBinding? At(DateTimeOffset at) =>
        Bindings.FirstOrDefault(binding =>
            binding.ObservedFrom <= at && (binding.ObservedTo is not { } to || at < to));
}

/// <summary>The live device holding this address now, reduced to what a resolution answers with.</summary>
/// <param name="Id">The device.</param>
/// <param name="Hostname">Its hostname, so a resolution need not be joined back to the inventory.</param>
/// <param name="CreatedAt">
/// When the device row was created. Used only as a tie-break: when a client observation also
/// covers the instant asked about and the device did not exist yet, the client is the honest
/// answer — a discovery candidate promoted last Tuesday cannot have been the asset on Monday.
/// </param>
internal sealed record CachedDevice(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("hostname")] string Hostname,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt);

/// <summary>One interval during which one client held this address.</summary>
/// <param name="ClientId">The client.</param>
/// <param name="MacAddress">Its hardware address.</param>
/// <param name="Source">What kind of observation established the binding.</param>
/// <param name="ObservedFrom">When the interval opened. Inclusive.</param>
/// <param name="ObservedTo">When it closed, or null while it is the current binding. Exclusive.</param>
internal sealed record CachedBinding(
    [property: JsonPropertyName("clientId")] Guid ClientId,
    [property: JsonPropertyName("macAddress")] string MacAddress,
    [property: JsonPropertyName("source")] ClientObservationSource Source,
    [property: JsonPropertyName("observedFrom")] DateTimeOffset ObservedFrom,
    [property: JsonPropertyName("observedTo")] DateTimeOffset? ObservedTo);
