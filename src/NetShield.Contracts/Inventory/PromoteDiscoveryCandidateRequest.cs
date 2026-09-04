namespace NetShield.Contracts.Inventory;

/// <summary>What a caller sends to turn a candidate into a device.</summary>
/// <remarks>
/// <para>
/// There is no address on the request. The address is the candidate — taking one from the caller
/// would let promotion create a device at an address no sweep ever found, which is what
/// <c>POST /api/v1/devices</c> is for.
/// </para>
/// <para>
/// <c>vendor</c>, <c>model</c>, <c>osVersion</c> and <c>serialNumber</c> are absent for the same
/// kind of reason: a sweep established none of them, and the walk that will is what fills them
/// in. The device is created as <c>Unknown</c> and stays that way until something has walked it.
/// </para>
/// </remarks>
/// <param name="Hostname">What to call the device.</param>
/// <param name="Site">Where it is, as free text.</param>
/// <param name="Role">What the device is for.</param>
/// <param name="Criticality">How much its failure matters.</param>
/// <param name="Environment">Which environment it belongs to.</param>
/// <param name="Owner">Who is responsible for it, as free text.</param>
/// <param name="Tags">Free-form labels.</param>
/// <param name="Notes">Anything else worth writing down.</param>
public sealed record PromoteDiscoveryCandidateRequest(
    string Hostname,
    string? Site,
    DeviceRole Role,
    CriticalityTier Criticality,
    DeviceEnvironment Environment,
    string? Owner,
    IReadOnlyList<string>? Tags,
    string? Notes);
