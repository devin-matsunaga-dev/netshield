import { describe, expect, it } from 'vitest';

import { hasActiveFilter, parseDeviceFilters } from '@/features/devices/api/deviceFilters';

/**
 * The filters are read from the URL, so what this does with a value nobody typed matters as much
 * as what it does with one somebody did: a hand-edited address, a stale bookmark and a link from
 * a colleague all arrive here first.
 */
describe('reading device filters out of a URL', () => {
  it('keeps a value the API offers', () => {
    expect(parseDeviceFilters({ state: 'Offline', vendor: 'CiscoIos' })).toEqual({
      state: 'Offline',
      vendor: 'CiscoIos',
    });
  });

  it('drops a value the API would refuse rather than sending it on', () => {
    expect(parseDeviceFilters({ state: 'Melted', criticality: 'Urgent' })).toEqual({});
  });

  it('drops a value of the wrong shape entirely', () => {
    expect(parseDeviceFilters({ state: 42, site: ['HQ'], search: null })).toEqual({});
  });

  it('is case sensitive about an enum, because the API is', () => {
    expect(parseDeviceFilters({ state: 'offline' })).toEqual({});
  });

  it('trims free text and treats whitespace as absent', () => {
    expect(parseDeviceFilters({ site: '  HQ  ', tag: '   ' })).toEqual({ site: 'HQ' });
  });

  it('reads descending from either the boolean or its string', () => {
    expect(parseDeviceFilters({ descending: true }).descending).toBe(true);
    expect(parseDeviceFilters({ descending: 'true' }).descending).toBe(true);
    expect(parseDeviceFilters({ descending: 'yes' }).descending).toBeUndefined();
  });

  it('keeps a sort field the API offers and drops one it does not', () => {
    expect(parseDeviceFilters({ sort: 'Hostname' }).sort).toBe('Hostname');
    expect(parseDeviceFilters({ sort: 'Serial' }).sort).toBeUndefined();
  });

  it('composes every filter at once', () => {
    expect(
      parseDeviceFilters({
        state: 'Warning',
        vendor: 'AristaEos',
        role: 'Switch',
        criticality: 'High',
        environment: 'Lab',
        site: 'HQ',
        tag: 'core',
        search: 'sw-0',
      }),
    ).toEqual({
      state: 'Warning',
      vendor: 'AristaEos',
      role: 'Switch',
      criticality: 'High',
      environment: 'Lab',
      site: 'HQ',
      tag: 'core',
      search: 'sw-0',
    });
  });
});

/** Which empty state to draw turns on this, and only on this. */
describe('whether anything is narrowing the list', () => {
  it('is false for no filters at all', () => {
    expect(hasActiveFilter({})).toBe(false);
  });

  it('is false when only the sort is set, because a sort narrows nothing', () => {
    expect(hasActiveFilter({ sort: 'Hostname', descending: true })).toBe(false);
  });

  it('is true for any narrowing filter', () => {
    expect(hasActiveFilter({ state: 'Offline' })).toBe(true);
    expect(hasActiveFilter({ site: 'HQ' })).toBe(true);
    expect(hasActiveFilter({ search: 'core' })).toBe(true);
  });
});
