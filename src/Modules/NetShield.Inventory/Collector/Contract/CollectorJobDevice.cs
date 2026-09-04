using NetShield.Contracts.Inventory;

namespace NetShield.Inventory.Collector.Contract;

/// <summary>
/// The device a leased job is about, in the four facts a collector needs to reach it.
/// </summary>
/// <remarks>
/// Not <c>DeviceSummary</c>. That shape exists for the devices screen and grows with it; this one
/// is the address to talk to and the vendor to talk to it as, and a collector should be handed
/// the estate's ownership, criticality and notes exactly never.
/// </remarks>
/// <param name="DeviceId">The device.</param>
/// <param name="Hostname">What it is called, for the collector's own log line.</param>
/// <param name="IpAddress">Where to reach it.</param>
/// <param name="Vendor">Which adapter to use, once there are adapters.</param>
internal sealed record CollectorJobDevice(
    Guid DeviceId,
    string Hostname,
    string IpAddress,
    DeviceVendor Vendor);
