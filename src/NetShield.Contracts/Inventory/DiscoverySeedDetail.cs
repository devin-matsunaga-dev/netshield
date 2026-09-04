namespace NetShield.Contracts.Inventory;

/// <summary>
/// A discovery seed in full: the ranges it sweeps, the addresses it leaves alone, and its
/// schedule.
/// </summary>
/// <remarks>
/// <para>
/// An exclusion is part of the seed rather than of the ignore list, and the two are different
/// things on purpose. An exclusion says "do not send a packet here" — a printer VLAN, a
/// neighbour's supernet, an address a firewall will complain about. The ignore list says "this
/// answered and I do not want to be asked about it again". One is about what NetShield probes;
/// the other is about what it shows a person.
/// </para>
/// <para>
/// A seed names no credential profile. Which credential a walk runs with is decided by
/// <c>Inventory:Discovery:CredentialKindOrder</c> at the moment a device is walked, so that
/// choosing a credential stays behind the <c>CredentialsManage</c> boundary WP-1.2 drew rather
/// than becoming something a seed can assign.
/// </para>
/// </remarks>
/// <param name="Id">The seed.</param>
/// <param name="Name">What an operator calls it.</param>
/// <param name="Description">Why it exists, as free text.</param>
/// <param name="Enabled">Whether the schedule runs it.</param>
/// <param name="Ranges">The CIDR ranges it sweeps, normalised.</param>
/// <param name="Exclusions">Addresses and ranges inside those that are never probed.</param>
/// <param name="AddressCount">How many addresses one run would sweep, after exclusions.</param>
/// <param name="IntervalMinutes">How often the schedule runs it.</param>
/// <param name="NextRunAt">When the schedule will next run it. UTC.</param>
/// <param name="LastRunAt">When it last started a run. UTC.</param>
/// <param name="CreatedAt">When it was created. UTC.</param>
/// <param name="UpdatedAt">When the row last changed. UTC.</param>
public sealed record DiscoverySeedDetail(
    Guid Id,
    string Name,
    string? Description,
    bool Enabled,
    IReadOnlyList<string> Ranges,
    IReadOnlyList<string> Exclusions,
    long AddressCount,
    int IntervalMinutes,
    DateTimeOffset? NextRunAt,
    DateTimeOffset? LastRunAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
