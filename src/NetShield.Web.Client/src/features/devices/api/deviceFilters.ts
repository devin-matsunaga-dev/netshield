import type { Schemas } from '@/api/types';

/** Everything the device list can be narrowed by. Every member is also a URL search parameter. */
export interface DeviceListFilters {
  readonly state?: Schemas['DeviceState'] | undefined;
  readonly vendor?: Schemas['DeviceVendor'] | undefined;
  readonly role?: Schemas['DeviceRole'] | undefined;
  readonly criticality?: Schemas['CriticalityTier'] | undefined;
  readonly environment?: Schemas['DeviceEnvironment'] | undefined;
  readonly site?: string | undefined;
  readonly tag?: string | undefined;
  readonly search?: string | undefined;
  readonly sort?: DeviceSortField | undefined;
  readonly descending?: boolean | undefined;
}

/** The sort fields the API offers. Anything else is refused with a 400 rather than ignored. */
export const deviceSortFields = [
  'CreatedAt',
  'UpdatedAt',
  'Hostname',
  'PrimaryIpAddress',
  'State',
  'Criticality',
] as const;

export type DeviceSortField = (typeof deviceSortFields)[number];

/**
 * Reads the filters out of a URL's search parameters.
 *
 * Anything unrecognised is dropped rather than passed on. A hand-edited address should leave the
 * reader on an unfiltered list, not on a 400 from the API — and a value the client did not
 * recognise is one the server would refuse anyway.
 */
export function parseDeviceFilters(search: Record<string, unknown>): DeviceListFilters {
  return {
    ...pick(search, 'state', deviceStates),
    ...pick(search, 'vendor', deviceVendors),
    ...pick(search, 'role', deviceRoles),
    ...pick(search, 'criticality', criticalityTiers),
    ...pick(search, 'environment', deviceEnvironments),
    ...pick(search, 'sort', deviceSortFields),
    ...text(search, 'site'),
    ...text(search, 'tag'),
    ...text(search, 'search'),
    ...(search['descending'] === true || search['descending'] === 'true'
      ? { descending: true }
      : {}),
  };
}

/** Whether anything is narrowing the list, which is what decides the empty state's wording. */
export function hasActiveFilter(filters: DeviceListFilters): boolean {
  return (
    filters.state !== undefined ||
    filters.vendor !== undefined ||
    filters.role !== undefined ||
    filters.criticality !== undefined ||
    filters.environment !== undefined ||
    filters.site !== undefined ||
    filters.tag !== undefined ||
    filters.search !== undefined
  );
}

export const deviceStates = ['Unknown', 'Online', 'Warning', 'Offline'] as const;

export const deviceVendors = [
  'Unknown',
  'CiscoIos',
  'CiscoNxOs',
  'JuniperJunOs',
  'AristaEos',
  'FortinetFortiOs',
  'MikroTikRouterOs',
  'GenericSnmp',
] as const;

export const deviceRoles = [
  'Router',
  'Switch',
  'Firewall',
  'AccessPoint',
  'LoadBalancer',
  'Server',
  'Other',
] as const;

export const criticalityTiers = ['Critical', 'High', 'Medium', 'Low'] as const;

export const deviceEnvironments = ['Production', 'Staging', 'Development', 'Lab'] as const;

function pick<T extends string>(
  search: Record<string, unknown>,
  name: string,
  permitted: readonly T[],
): Record<string, T> {
  const value = search[name];

  return typeof value === 'string' && (permitted as readonly string[]).includes(value)
    ? { [name]: value as T }
    : {};
}

function text(search: Record<string, unknown>, name: string): Record<string, string> {
  const value = search[name];

  return typeof value === 'string' && value.trim().length > 0 ? { [name]: value.trim() } : {};
}
