using System.Net;

using NetShield.Contracts.Inventory;

namespace NetShield.Inventory.Discovery;

/// <summary>
/// One address that answered a run's sweep, and what NetShield did about it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Only responders are recorded.</strong> A run keeps the ranges it swept and how many
/// addresses that was, so "was this address in scope" stays answerable from the run; writing a
/// row for every silent address would put tens of thousands of rows per run into a table nothing
/// prunes yet, each one saying that nothing happened. An outcome row is an observation, and
/// silence is the absence of one.
/// </para>
/// <para>
/// It is the run's own record and is never updated: a re-run writes new rows against a new run,
/// and it is <see cref="DiscoveryCandidate"/> that carries the standing view of an address.
/// </para>
/// </remarks>
internal sealed class DiscoveryRunHost
{
    /// <summary>UUID v7, so the primary key is also the order observations were applied in.</summary>
    public Guid Id { get; init; }

    /// <summary>The run that observed it.</summary>
    public Guid RunId { get; init; }

    /// <summary>The address that answered.</summary>
    public IPAddress Address { get; init; } = IPAddress.None;

    /// <summary>The round trip of the reply, when one was timed.</summary>
    public double? RttMilliseconds { get; init; }

    /// <summary>What NetShield did about it.</summary>
    public DiscoveryHostOutcome Outcome { get; init; }

    /// <summary>The candidate it created or refreshed, when it did either.</summary>
    public Guid? CandidateId { get; init; }

    /// <summary>The device it already belongs to, when it does.</summary>
    public Guid? DeviceId { get; init; }

    /// <summary>When the result was applied. UTC.</summary>
    public DateTimeOffset ObservedAt { get; init; }

    /// <summary>When the row was created. UTC.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the row last changed. UTC.</summary>
    public DateTimeOffset UpdatedAt { get; init; }
}
