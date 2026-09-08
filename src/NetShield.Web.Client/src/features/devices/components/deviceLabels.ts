import type { Schemas } from '@/api/types';

/**
 * What each enum member is called on screen.
 *
 * The wire carries a name — `"CiscoIos"` — because WP-0.4 settled that an enum travels as its
 * name so a member inserted later cannot renumber what a stored response meant. A name is not a
 * label, though: sentence case is the rule (DESIGN.md §4) and "CiscoIos" is neither sentence
 * case nor how anybody writes it. These tables are the one place the two are reconciled.
 */
export const vendorLabels: Record<Schemas['DeviceVendor'], string> = {
  Unknown: 'Not identified',
  CiscoIos: 'Cisco IOS',
  CiscoNxOs: 'Cisco NX-OS',
  JuniperJunOs: 'Juniper JunOS',
  AristaEos: 'Arista EOS',
  FortinetFortiOs: 'Fortinet FortiOS',
  MikroTikRouterOs: 'MikroTik RouterOS',
  GenericSnmp: 'Generic SNMP',
};

export const roleLabels: Record<Schemas['DeviceRole'], string> = {
  Router: 'Router',
  Switch: 'Switch',
  Firewall: 'Firewall',
  AccessPoint: 'Access point',
  LoadBalancer: 'Load balancer',
  Server: 'Server',
  Other: 'Other',
};

export const criticalityLabels: Record<Schemas['CriticalityTier'], string> = {
  Critical: 'Critical',
  High: 'High',
  Medium: 'Medium',
  Low: 'Low',
};

export const environmentLabels: Record<Schemas['DeviceEnvironment'], string> = {
  Production: 'Production',
  Staging: 'Staging',
  Development: 'Development',
  Lab: 'Lab',
};

export const interfaceStatusLabels: Record<Schemas['InterfaceStatus'], string> = {
  Up: 'Up',
  Down: 'Down',
  Testing: 'Testing',
  Dormant: 'Dormant',
  NotPresent: 'Not present',
  LowerLayerDown: 'Lower layer down',
  Unknown: 'Unknown',
};

/**
 * A speed in bits per second, as a person would write it. `null` when the walk could not
 * establish one — a saturated 32-bit gauge with no high-speed counter beside it is the absence
 * of a measurement rather than a 4.29 Gbit/s port.
 */
export function formatSpeed(bitsPerSecond: number | null | undefined): string {
  if (bitsPerSecond === null || bitsPerSecond === undefined) {
    return '—';
  }

  const units = [
    { limit: 1_000_000_000_000, suffix: 'Tbit/s' },
    { limit: 1_000_000_000, suffix: 'Gbit/s' },
    { limit: 1_000_000, suffix: 'Mbit/s' },
    { limit: 1_000, suffix: 'kbit/s' },
  ];

  for (const unit of units) {
    if (bitsPerSecond >= unit.limit) {
      const value = bitsPerSecond / unit.limit;

      return `${value % 1 === 0 ? value.toString() : value.toFixed(1)} ${unit.suffix}`;
    }
  }

  return `${bitsPerSecond.toString()} bit/s`;
}

/**
 * An uptime in seconds, as days and hours.
 *
 * `sysUpTime` is a 32-bit counter of hundredths of a second and wraps after about 497 days, so a
 * device up longer reports a small number. Nothing here reconstructs a boot time from it: that
 * needs a second observation to disambiguate the wrap, which Phase 3 will have and this does not.
 */
export function formatUptime(seconds: number | null | undefined): string {
  if (seconds === null || seconds === undefined) {
    return '—';
  }

  const days = Math.floor(seconds / 86_400);
  const hours = Math.floor((seconds % 86_400) / 3_600);

  return days > 0 ? `${days.toString()} d ${hours.toString()} h` : `${hours.toString()} h`;
}
