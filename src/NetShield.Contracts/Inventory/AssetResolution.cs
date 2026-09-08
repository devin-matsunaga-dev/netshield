namespace NetShield.Contracts.Inventory;

/// <summary>
/// What held an address at an instant, and the evidence for saying so.
/// </summary>
/// <remarks>
/// <para>
/// The answer <c>ResolveAssetAt(ip, timestamp)</c> gives, and the shape every Phase 4 flow record
/// and Phase 5 log event will be enriched from. It carries the interval the answer came out of
/// as well as the answer itself, because a resolution nobody can check is one nobody should
/// trust — an operator looking at a flow attributed to the wrong host needs to see which
/// observation attributed it.
/// </para>
/// <para>
/// <strong>It carries no observation source, deliberately.</strong> The obvious member would be a
/// nullable <see cref="ClientObservationSource"/> — absent when no observation covered the
/// instant — and a nullable enum member contaminates the <em>shared</em> schema in the OpenAPI
/// document: ASP.NET Core appends <c>null</c> to the enum itself, so every other shape using that
/// enum starts reading as nullable in the generated client when it never is. Which MIB table said
/// so is diagnostic rather than part of an attribution, and it is on the binding summaries, where
/// it is always present and always honest.
/// </para>
/// <para>
/// <strong>Client resolution is time-accurate; device resolution is not.</strong> A client's
/// address history is a chain of closed intervals, so an address reassigned between two hosts
/// resolves correctly on either side of the handover. A device has no address history — WP-1.1
/// gave <c>devices</c> one address and no record of what it held before — so a device is resolved
/// from the address it is reached on <em>now</em>, whatever the timestamp. Adding device address
/// history means changing the device update path and the API contract, and it belongs to a
/// package told to.
/// </para>
/// </remarks>
/// <param name="IpAddress">The address that was asked about, normalised.</param>
/// <param name="At">The instant it was asked about. UTC.</param>
/// <param name="Kind">What the address resolved to.</param>
/// <param name="DeviceId">The device, when one held it.</param>
/// <param name="DeviceHostname">That device's hostname, so a caller need not read it back.</param>
/// <param name="ClientId">The client, when one held it.</param>
/// <param name="MacAddress">
/// The client's MAC. Present whenever a client observation covered the instant, including when
/// the address also belongs to a device — the MAC is then extra evidence about the same asset.
/// </param>
/// <param name="ObservedFrom">
/// When the binding opened — the instant the observation that opened it was applied, not the
/// instant the handover truly happened. A binding is opened by a poll, so the true change fell
/// somewhere in the window before this.
/// </param>
/// <param name="ObservedTo">
/// When the binding closed, or <see langword="null"/> while it is still the current one. A
/// closed binding ends exactly where its successor begins, so no instant falls in a gap and no
/// instant falls in two bindings at once.
/// </param>
public sealed record AssetResolution(
    string IpAddress,
    DateTimeOffset At,
    AssetKind Kind,
    Guid? DeviceId,
    string? DeviceHostname,
    Guid? ClientId,
    string? MacAddress,
    DateTimeOffset? ObservedFrom,
    DateTimeOffset? ObservedTo);
