using NetShield.Contracts.Messaging;

namespace NetShield.Contracts.Inventory.Events;

/// <summary>Every sweep job of a discovery run has reported.</summary>
/// <remarks>
/// <para>
/// Published once, when the last of the run's jobs is applied — including when some or all of
/// them failed, because "the run finished and found nothing because every sweep failed" is
/// exactly the fact a subscriber needs and is not the same as the run still being in flight.
/// <see cref="Status"/> is what tells the two apart.
/// </para>
/// <para>
/// It carries counts rather than the addresses. An outbox row is readable by every module, and a
/// payload wide enough to list a /16's responders would put the estate's address map in a column
/// all of them can read — the reasoning WP-1.3 settled for <c>CollectorJobCompleted</c>.
/// </para>
/// </remarks>
/// <param name="RunId">The run.</param>
/// <param name="SeedId">The seed it swept.</param>
/// <param name="Status">Whether every sweep job got through, some, or none.</param>
/// <param name="AddressCount">How many addresses it set out to sweep.</param>
/// <param name="RespondedCount">How many answered.</param>
/// <param name="NewCandidateCount">How many of those nobody had seen before.</param>
/// <param name="CompletedAt">When the last sweep job was applied. UTC.</param>
public sealed record DiscoveryRunCompleted(
    Guid RunId,
    Guid SeedId,
    DiscoveryRunStatus Status,
    long AddressCount,
    int RespondedCount,
    int NewCandidateCount,
    DateTimeOffset CompletedAt) : IIntegrationEvent;
