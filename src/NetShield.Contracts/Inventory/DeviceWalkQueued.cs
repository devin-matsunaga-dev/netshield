namespace NetShield.Contracts.Inventory;

/// <summary>
/// The answer to an on-demand fingerprint request: a walk has been queued, not performed.
/// </summary>
/// <remarks>
/// <para>
/// The route answers <c>202</c> and this shape, rather than <c>200</c> and a fingerprint,
/// because the API does not talk to devices — it schedules, and a collector leases the work when
/// it next asks (ARCHITECTURE.md §7). What a caller does with <see cref="JobId"/> is watch for
/// the device's own record to change; there is no job-status route in V1 and this package does
/// not invent one.
/// </para>
/// <para>
/// It deliberately does not say <em>which</em> credential profile was chosen. The route is gated
/// on <c>DiscoveryRun</c>, which the Operator role holds and which says nothing about
/// credentials; WP-1.2 settled that even the list of profile names is behind
/// <c>CredentialsManage</c>, because a profile's identity is half of a statement about which
/// accounts NetShield holds passwords for. The choice is recorded on the job row and in the
/// log — both of which are already privileged — rather than handed back over the API.
/// </para>
/// </remarks>
/// <param name="JobId">The queued job.</param>
/// <param name="DeviceId">The device it will walk.</param>
/// <param name="QueuedAt">When the job was queued. UTC.</param>
public sealed record DeviceWalkQueued(
    Guid JobId,
    Guid DeviceId,
    DateTimeOffset QueuedAt);
