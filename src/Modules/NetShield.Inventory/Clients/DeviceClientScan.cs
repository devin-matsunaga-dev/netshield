namespace NetShield.Inventory.Clients;

/// <summary>
/// What NetShield knows about reading one device's client tables, and when it will ask again.
/// </summary>
/// <remarks>
/// <para>
/// The same shape as <c>device_reachability</c> and <c>device_fingerprints</c>, and there for the
/// same three reasons: the schedule needs somewhere to record when a device is next due, the
/// result handler needs somewhere to record the job it last applied so that an at-least-once
/// redelivery is a no-op, and a walk that could not be performed needs somewhere to say so that
/// is not the client tables themselves.
/// </para>
/// <para>
/// <see cref="NeighborsSupported"/> and <see cref="ForwardingSupported"/> are the answer to a
/// question an operator will ask the first time a switch shows no clients: <em>did this device
/// not answer, or does it genuinely have nothing?</em> A router implements a neighbour cache and
/// no forwarding database, an access switch the reverse, and a layer-3 switch both — so an
/// absence is only meaningful once you know which of the two the device speaks at all.
/// </para>
/// </remarks>
internal sealed class DeviceClientScan
{
    /// <summary>UUID v7.</summary>
    public Guid Id { get; init; }

    /// <summary>The device this is about. Unique — a device has one scan row.</summary>
    public Guid DeviceId { get; init; }

    /// <summary>The earliest the next walk should be queued. UTC.</summary>
    public DateTimeOffset NextWalkAt { get; set; }

    /// <summary>When a walk last reported, successfully or not. UTC.</summary>
    public DateTimeOffset? LastWalkAt { get; set; }

    /// <summary>
    /// The job whose result was last applied to this device's bindings.
    /// </summary>
    /// <remarks>
    /// Outbox delivery is at-least-once, and applying one walk twice would be worse here than
    /// almost anywhere else in NetShield: the second application would find the bindings the
    /// first one opened, see no conflict, and quietly do nothing — or, if a competing observation
    /// had arrived between the two, close and reopen an interval on evidence that was already
    /// spent. Dropping a redelivered job is what stops both.
    /// </remarks>
    public Guid? LastAppliedJobId { get; set; }

    /// <summary>Whether the device answered an ARP or neighbour table at the last walk.</summary>
    public bool? NeighborsSupported { get; set; }

    /// <summary>Whether it answered a forwarding database.</summary>
    public bool? ForwardingSupported { get; set; }

    /// <summary>How many neighbour entries the last walk read.</summary>
    public int? LastNeighborCount { get; set; }

    /// <summary>How many forwarding entries it read.</summary>
    public int? LastForwardingCount { get; set; }

    /// <summary>
    /// Why the last walk could not be performed, or <see langword="null"/> if the last one ran.
    /// This is the collector's health, and it deliberately reaches no binding.
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>When the row was created. UTC.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the row last changed. UTC.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
