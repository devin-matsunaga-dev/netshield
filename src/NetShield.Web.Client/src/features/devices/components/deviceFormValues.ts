import type { Schemas } from '@/api/types';
import type { DeviceFormValues } from '@/features/devices/components/DeviceForm';

/**
 * What the form's text fields become on the wire: an empty box is an absent value, not `""`.
 *
 * Absent is `null` rather than `undefined` because the API's update is whole-resource
 * replacement (WP-1.1) — an omitted member and a member set to nothing have to be the same
 * thing, and `null` is the one of the two that survives being serialised.
 */
export function toRequest(values: DeviceFormValues) {
  const optional = (value: string): string | null =>
    value.trim().length === 0 ? null : value.trim();

  return {
    hostname: values.hostname.trim(),
    primaryIpAddress: values.primaryIpAddress.trim(),
    vendor: values.vendor,
    model: optional(values.model),
    osVersion: optional(values.osVersion),
    serialNumber: optional(values.serialNumber),
    site: optional(values.site),
    role: values.role,
    criticality: values.criticality,
    environment: values.environment,
    owner: optional(values.owner),
    tags: values.tags
      .split(',')
      .map((tag) => tag.trim())
      .filter((tag) => tag.length > 0),
    notes: optional(values.notes),
  };
}

/** The form's starting values for a device that already exists. */
export function fromDetail(device: Schemas['DeviceDetail']): DeviceFormValues {
  return {
    hostname: device.hostname,
    primaryIpAddress: device.primaryIpAddress,
    vendor: device.vendor,
    model: device.model ?? '',
    osVersion: device.osVersion ?? '',
    serialNumber: device.serialNumber ?? '',
    site: device.site ?? '',
    role: device.role,
    criticality: device.criticality,
    environment: device.environment,
    owner: device.owner ?? '',
    tags: device.tags.join(', '),
    notes: device.notes ?? '',
  };
}
