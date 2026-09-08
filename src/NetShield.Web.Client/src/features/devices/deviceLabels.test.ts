import { describe, expect, it } from 'vitest';

import type { Schemas } from '@/api/types';
import {
  criticalityLabels,
  environmentLabels,
  formatSpeed,
  formatUptime,
  interfaceStatusLabels,
  roleLabels,
  vendorLabels,
} from '@/features/devices/components/deviceLabels';
import {
  criticalityTiers,
  deviceEnvironments,
  deviceRoles,
  deviceVendors,
} from '@/features/devices/api/deviceFilters';

/**
 * The wire carries an enum's name, which is not a label: `"CiscoIos"` is neither sentence case
 * (DESIGN.md §4) nor how anybody writes it. A member added to the contract with no label here
 * would render as its raw name, so these assert every member has one.
 */
describe('the enum labels', () => {
  it('names every vendor', () => {
    for (const vendor of deviceVendors) {
      expect(vendorLabels[vendor]).toBeTruthy();
    }

    expect(vendorLabels.CiscoNxOs).toBe('Cisco NX-OS');
    expect(vendorLabels.Unknown).toBe('Not identified');
  });

  it('names every role, criticality and environment', () => {
    for (const role of deviceRoles) {
      expect(roleLabels[role]).toBeTruthy();
    }

    for (const tier of criticalityTiers) {
      expect(criticalityLabels[tier]).toBeTruthy();
    }

    for (const environment of deviceEnvironments) {
      expect(environmentLabels[environment]).toBeTruthy();
    }

    // Sentence case, not the contract's PascalCase.
    expect(roleLabels.AccessPoint).toBe('Access point');
    expect(roleLabels.LoadBalancer).toBe('Load balancer');
  });

  it('names every interface status', () => {
    const statuses: Schemas['InterfaceStatus'][] = [
      'Unknown',
      'Up',
      'Down',
      'Testing',
      'Dormant',
      'NotPresent',
      'LowerLayerDown',
    ];

    for (const status of statuses) {
      expect(interfaceStatusLabels[status]).toBeTruthy();
    }

    expect(interfaceStatusLabels.LowerLayerDown).toBe('Lower layer down');
  });
});

describe('writing a link speed', () => {
  it('uses the unit a person would', () => {
    expect(formatSpeed(1_000_000_000)).toBe('1 Gbit/s');
    expect(formatSpeed(10_000_000_000)).toBe('10 Gbit/s');
    expect(formatSpeed(100_000_000)).toBe('100 Mbit/s');
    expect(formatSpeed(1_500_000_000)).toBe('1.5 Gbit/s');
    expect(formatSpeed(400_000_000_000)).toBe('400 Gbit/s');
  });

  it('falls back to bits for something very slow', () => {
    expect(formatSpeed(64)).toBe('64 bit/s');
  });

  /**
   * The walk reports no speed at all when a saturated 32-bit `ifSpeed` had no `ifHighSpeed`
   * beside it, because 4.29 Gbit/s for a 100G port is a measurement rather than the absence of
   * one. An em dash is the honest rendering of that.
   */
  it('shows an em dash when the walk established no speed', () => {
    expect(formatSpeed(null)).toBe('—');
    expect(formatSpeed(undefined)).toBe('—');
  });
});

describe('writing an uptime', () => {
  it('gives days and hours', () => {
    expect(formatUptime(90_000)).toBe('1 d 1 h');
    expect(formatUptime(1_234_567)).toBe('14 d 6 h');
  });

  it('gives hours alone under a day', () => {
    expect(formatUptime(7_200)).toBe('2 h');
    expect(formatUptime(60)).toBe('0 h');
  });

  it('shows an em dash when the agent answered none', () => {
    expect(formatUptime(null)).toBe('—');
  });
});
