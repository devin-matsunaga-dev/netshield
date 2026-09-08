using NetShield.Contracts.Inventory;

namespace NetShield.Inventory.Clients;

/// <summary>
/// Turns the client entities into the shapes that leave the module. The one place the boundary in
/// ARCHITECTURE.md §4 is crossed for this feature, and the reason nothing outside it needs to see
/// <see cref="Client"/>, <see cref="ClientIpBinding"/> or <see cref="ClientPortBinding"/>.
/// </summary>
internal static class ClientMapping
{
    /// <summary>
    /// A row of the client list. The current address and port are passed in rather than read off
    /// the client, because nothing current is stored on it — the open interval is the answer, and
    /// the list query joins it.
    /// </summary>
    internal static ClientSummary ToSummary(
        this Client client,
        ClientIpBinding? address,
        ClientPortBinding? port,
        string? deviceHostname) =>
        new(
            client.Id,
            client.MacAddress,
            client.Oui,
            client.LocallyAdministered,
            client.Hostname,
            address?.IpAddress.ToString(),
            port?.DeviceId,
            deviceHostname,
            port?.IfIndex,
            port?.VlanId,
            client.FirstSeenAt,
            client.LastSeenAt);

    internal static ClientDetail ToDetail(
        this Client client,
        IReadOnlyList<ClientIpBindingSummary> addresses,
        IReadOnlyList<ClientPortBindingSummary> ports) =>
        new(
            client.Id,
            client.MacAddress,
            client.Oui,
            client.LocallyAdministered,
            client.Hostname,
            addresses,
            ports,
            client.FirstSeenAt,
            client.LastSeenAt);

    internal static ClientIpBindingSummary ToSummary(
        this ClientIpBinding binding,
        string? deviceHostname) =>
        new(
            binding.Id,
            binding.IpAddress.ToString(),
            binding.Source,
            binding.DeviceId,
            deviceHostname,
            binding.ObservedFrom,
            binding.ObservedTo,
            binding.LastSeenAt);

    internal static ClientPortBindingSummary ToSummary(
        this ClientPortBinding binding,
        string? deviceHostname,
        string? interfaceName) =>
        new(
            binding.Id,
            binding.DeviceId,
            deviceHostname,
            binding.IfIndex,
            interfaceName,
            binding.VlanId,
            binding.MacCountOnPort,
            binding.Source,
            binding.ObservedFrom,
            binding.ObservedTo,
            binding.LastSeenAt);
}
