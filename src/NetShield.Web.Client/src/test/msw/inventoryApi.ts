import { http, HttpResponse, type RequestHandler } from 'msw';

import type { Schemas } from '@/api/types';

type DeviceSummary = Schemas['DeviceSummary'];
type DeviceDetail = Schemas['DeviceDetail'];
type DeviceFingerprintDetail = Schemas['DeviceFingerprintDetail'];
type DeviceInterfaceSummary = Schemas['DeviceInterfaceSummary'];
type DeviceReachabilityDetail = Schemas['DeviceReachabilityDetail'];
type DiscoveryCandidateSummary = Schemas['DiscoveryCandidateSummary'];
type DiscoveryRunSummary = Schemas['DiscoveryRunSummary'];
type DiscoveryRunDetail = Schemas['DiscoveryRunDetail'];
type DiscoveryRunHostResult = Schemas['DiscoveryRunHostResult'];
type DiscoverySeedSummary = Schemas['DiscoverySeedSummary'];
type DiscoveryIgnoreEntry = Schemas['DiscoveryIgnoreEntry'];
type CredentialProfileSummary = Schemas['CredentialProfileSummary'];

/**
 * The inventory API a test sees.
 *
 * `GET /api/v1/credential-profiles` is deliberately *not* here: WP-1.9 gave that resource a
 * module of its own, and two handler files claiming one route means whichever is registered
 * first silently wins. What is here is the device's *assignment* of profiles, which is an
 * inventory concern.
 *
 * Every shape here is the generated one, so a fixture that drifts from the contract fails to
 * type-check rather than passing a test that lies about what the API sends. The numeric members
 * are written as plain numbers, which is what the API actually writes — the contract's
 * `number | string` is the document describing what it will *accept*.
 */
export interface InventoryApiState {
  devices: DeviceSummary[];
  detail: Map<string, DeviceDetail>;
  fingerprints: Map<string, DeviceFingerprintDetail>;
  interfaces: Map<string, DeviceInterfaceSummary[]>;
  reachability: Map<string, DeviceReachabilityDetail>;
  deviceProfiles: Map<string, CredentialProfileSummary[]>;
  candidates: DiscoveryCandidateSummary[];
  runs: DiscoveryRunSummary[];
  runDetail: Map<string, DiscoveryRunDetail>;
  runHosts: Map<string, DiscoveryRunHostResult[]>;
  seeds: DiscoverySeedSummary[];
  ignores: DiscoveryIgnoreEntry[];
  /** Every write the SPA made, in order, so a test can assert on what was actually sent. */
  readonly writes: { method: string; path: string; body: unknown }[];
  /** Turn a read into a 500, to reach the error state (DESIGN.md §8). */
  failDeviceList: boolean;
}

const at = '2026-09-08T09:00:00.000Z';

/** One device, with everything defaulted so a test names only what it is about. */
export function makeDevice(overrides: Partial<DeviceSummary> = {}): DeviceSummary {
  return {
    id: '019226b4-1000-7000-8000-000000000001',
    hostname: 'core-sw-01',
    primaryIpAddress: '10.0.0.1',
    vendor: 'CiscoIos',
    model: 'C9300-48P',
    role: 'Switch',
    site: 'HQ',
    criticality: 'Critical',
    environment: 'Production',
    state: 'Online',
    tags: ['core'],
    updatedAt: at,
    ...overrides,
  };
}

export function makeDetail(overrides: Partial<DeviceDetail> = {}): DeviceDetail {
  return {
    id: '019226b4-1000-7000-8000-000000000001',
    hostname: 'core-sw-01',
    primaryIpAddress: '10.0.0.1',
    vendor: 'CiscoIos',
    model: 'C9300-48P',
    osVersion: '17.9.4',
    serialNumber: 'FOC1234X5YZ',
    site: 'HQ',
    role: 'Switch',
    criticality: 'Critical',
    environment: 'Production',
    owner: 'Network team',
    tags: ['core'],
    notes: null,
    state: 'Online',
    createdAt: at,
    updatedAt: at,
    ...overrides,
  };
}

export function makeFingerprint(
  overrides: Partial<DeviceFingerprintDetail> = {},
): DeviceFingerprintDetail {
  return {
    deviceId: '019226b4-1000-7000-8000-000000000001',
    vendor: 'CiscoIos',
    reducedCapability: false,
    sysObjectId: '1.3.6.1.4.1.9.1.2494',
    sysDescr: 'Cisco IOS Software, Version 17.9.4',
    sysName: 'core-sw-01',
    sysContact: 'netops@example.invalid',
    sysLocation: 'Rack 3',
    uptimeSeconds: 1_234_567,
    model: 'C9300-48P',
    osVersion: '17.9.4',
    serialNumber: 'FOC1234X5YZ',
    interfaceCount: 2,
    interfacesTruncated: false,
    overriddenFields: [],
    lastWalkAt: at,
    lastError: null,
    updatedAt: at,
    ...overrides,
  };
}

export function makeReachability(
  overrides: Partial<DeviceReachabilityDetail> = {},
): DeviceReachabilityDetail {
  return {
    deviceId: '019226b4-1000-7000-8000-000000000001',
    state: 'Online',
    lastRttMilliseconds: 2.5,
    lastLossPercent: 0,
    lastProbeAt: at,
    lastChangedAt: at,
    nextProbeAt: at,
    lastError: null,
    ...overrides,
  };
}

export function makeInterface(
  overrides: Partial<DeviceInterfaceSummary> = {},
): DeviceInterfaceSummary {
  return {
    id: '019226b4-2000-7000-8000-000000000001',
    ifIndex: 1,
    name: 'Gi1/0/1',
    description: 'GigabitEthernet1/0/1',
    alias: 'uplink to core',
    interfaceType: 6,
    mtu: 1500,
    speedBitsPerSecond: 1_000_000_000,
    physicalAddress: '00:1A:2B:3C:4D:01',
    adminStatus: 'Up',
    operStatus: 'Up',
    firstSeenAt: at,
    lastSeenAt: at,
    ...overrides,
  };
}

export function makeCandidate(
  overrides: Partial<DiscoveryCandidateSummary> = {},
): DiscoveryCandidateSummary {
  return {
    id: '019226b4-3000-7000-8000-000000000001',
    address: '192.0.2.37',
    status: 'New',
    timesSeen: 3,
    lastRttMilliseconds: 1.2,
    firstSeenAt: at,
    lastSeenAt: at,
    firstSeenRunId: '019226b4-4000-7000-8000-000000000001',
    lastSeenRunId: '019226b4-4000-7000-8000-000000000001',
    promotedDeviceId: null,
    ...overrides,
  };
}

export function makeRun(overrides: Partial<DiscoveryRunSummary> = {}): DiscoveryRunSummary {
  return {
    id: '019226b4-4000-7000-8000-000000000001',
    seedId: '019226b4-5000-7000-8000-000000000001',
    seedName: 'Campus core',
    trigger: 'Scheduled',
    status: 'Completed',
    addressCount: 254,
    respondedCount: 3,
    newCandidateCount: 1,
    startedAt: at,
    completedAt: at,
    ...overrides,
  };
}

export function makeRunDetail(overrides: Partial<DiscoveryRunDetail> = {}): DiscoveryRunDetail {
  return {
    id: '019226b4-4000-7000-8000-000000000001',
    seedId: '019226b4-5000-7000-8000-000000000001',
    seedName: 'Campus core',
    trigger: 'Scheduled',
    status: 'Completed',
    ranges: ['10.0.0.0/24'],
    exclusions: [],
    addressCount: 254,
    jobCount: 1,
    jobsCompleted: 1,
    jobsFailed: 0,
    respondedCount: 3,
    newCandidateCount: 1,
    knownCandidateCount: 1,
    existingDeviceCount: 1,
    ignoredCount: 0,
    startedAt: at,
    completedAt: at,
    ...overrides,
  };
}

export function makeSeed(overrides: Partial<DiscoverySeedSummary> = {}): DiscoverySeedSummary {
  return {
    id: '019226b4-5000-7000-8000-000000000001',
    name: 'Campus core',
    enabled: true,
    rangeCount: 1,
    addressCount: 254,
    intervalMinutes: 1440,
    nextRunAt: at,
    lastRunAt: at,
    updatedAt: at,
    ...overrides,
  };
}

export function createInventoryApi(overrides: Partial<InventoryApiState> = {}): InventoryApiState {
  return {
    devices: [],
    detail: new Map(),
    fingerprints: new Map(),
    interfaces: new Map(),
    reachability: new Map(),
    deviceProfiles: new Map(),
    candidates: [],
    runs: [],
    runDetail: new Map(),
    runHosts: new Map(),
    seeds: [],
    ignores: [],
    writes: [],
    failDeviceList: false,
    ...overrides,
  };
}

/**
 * The handlers, reading whatever `current()` returns so a test can replace the whole state
 * between runs without re-registering anything.
 *
 * The device list applies the filters it is given, because "filters compose and survive a
 * refresh" is a WP-1.7 criterion and a handler that ignored them would let a broken filter pass.
 */
export function inventoryHandlers(
  current: () => InventoryApiState,
  /**
   * Which credential profiles exist, which the credentials module owns.
   *
   * Passed in rather than duplicated, because a device's *assignment* is an inventory fact while
   * the profiles it assigns are not — and two copies of the same list is how a fixture comes to
   * assign a profile the profile list has never heard of.
   */
  profiles: () => readonly CredentialProfileSummary[],
): RequestHandler[] {
  return [
    http.get('/api/v1/devices', ({ request }) => {
      const state = current();

      if (state.failDeviceList) {
        return HttpResponse.json({ status: 500, title: 'Server error' }, { status: 500 });
      }

      const query = new URL(request.url).searchParams;

      const matches = state.devices.filter(
        (device) =>
          equals(query.get('state'), device.state) &&
          equals(query.get('vendor'), device.vendor) &&
          equals(query.get('role'), device.role) &&
          equals(query.get('criticality'), device.criticality) &&
          equals(query.get('environment'), device.environment) &&
          equalsText(query.get('site'), device.site) &&
          contains(query.get('search'), device.hostname),
      );

      return HttpResponse.json({
        items: matches,
        nextCursor: null,
        totalCount: matches.length,
      });
    }),

    http.get('/api/v1/devices/:id', ({ params }) => {
      const device = current().detail.get(String(params['id']));

      return device === undefined ? notFound('device.not-found') : HttpResponse.json(device);
    }),

    http.get('/api/v1/devices/:id/fingerprint', ({ params }) => {
      const fingerprint = current().fingerprints.get(String(params['id']));

      return fingerprint === undefined
        ? notFound('discovery.fingerprint-not-found')
        : HttpResponse.json(fingerprint);
    }),

    http.get('/api/v1/devices/:id/interfaces', ({ params }) =>
      HttpResponse.json({
        items: current().interfaces.get(String(params['id'])) ?? [],
        nextCursor: null,
        totalCount: (current().interfaces.get(String(params['id'])) ?? []).length,
      }),
    ),

    http.get('/api/v1/devices/:id/reachability', ({ params }) => {
      const reachability = current().reachability.get(String(params['id']));

      return reachability === undefined
        ? notFound('reachability.not-found')
        : HttpResponse.json(reachability);
    }),

    http.get('/api/v1/devices/:deviceId/credential-profiles', ({ params }) =>
      HttpResponse.json(current().deviceProfiles.get(String(params['deviceId'])) ?? []),
    ),

    http.put('/api/v1/devices/:deviceId/credential-profiles', async ({ params, request }) => {
      const state = current();
      const body = await request.json();

      state.writes.push({
        method: 'PUT',
        path: `/devices/${String(params['deviceId'])}/credential-profiles`,
        body,
      });

      const ids = (body as { credentialProfileIds: string[] }).credentialProfileIds;

      state.deviceProfiles.set(
        String(params['deviceId']),
        profiles().filter((profile) => ids.includes(profile.id)),
      );

      return HttpResponse.json(state.deviceProfiles.get(String(params['deviceId'])) ?? []);
    }),

    http.post('/api/v1/devices', async ({ request }) => {
      const state = current();
      const body = await request.json();

      state.writes.push({ method: 'POST', path: '/devices', body });

      const sent = body as { hostname: string; primaryIpAddress: string };

      if (state.devices.some((device) => device.primaryIpAddress === sent.primaryIpAddress)) {
        return HttpResponse.json(
          {
            status: 409,
            title: 'Conflict',
            detail: `Another device is already at ${sent.primaryIpAddress}.`,
            code: 'device.duplicate-primary-ip',
          },
          { status: 409 },
        );
      }

      const created = makeDetail({
        id: `019226b4-1000-7000-8000-00000000${(state.devices.length + 10).toString()}`,
        hostname: sent.hostname,
        primaryIpAddress: sent.primaryIpAddress,
        state: 'Unknown',
        vendor: 'Unknown',
      });

      state.detail.set(created.id, created);
      state.devices.push(makeDevice({ ...created, state: 'Unknown', vendor: 'Unknown' }));

      return HttpResponse.json(created, { status: 201 });
    }),

    http.put('/api/v1/devices/:id', async ({ params, request }) => {
      const state = current();
      const body = await request.json();
      const id = String(params['id']);

      state.writes.push({ method: 'PUT', path: `/devices/${id}`, body });

      const existing = state.detail.get(id);

      if (existing === undefined) {
        return notFound('device.not-found');
      }

      const updated = { ...existing, ...(body as Partial<DeviceDetail>) };

      state.detail.set(id, updated);

      return HttpResponse.json(updated);
    }),

    http.delete('/api/v1/devices/:id', ({ params }) => {
      const state = current();
      const id = String(params['id']);

      state.writes.push({ method: 'DELETE', path: `/devices/${id}`, body: null });
      state.detail.delete(id);
      state.devices = state.devices.filter((device) => device.id !== id);

      return new HttpResponse(null, { status: 204 });
    }),

    http.post('/api/v1/devices/:id/walk', ({ params }) => {
      const state = current();

      state.writes.push({
        method: 'POST',
        path: `/devices/${String(params['id'])}/walk`,
        body: null,
      });

      return HttpResponse.json(
        {
          deviceId: String(params['id']),
          jobId: '019226b4-6000-7000-8000-000000000001',
          queuedAt: at,
        },
        { status: 202 },
      );
    }),

    http.get('/api/v1/discovery/candidates', ({ request }) => {
      const status = new URL(request.url).searchParams.get('status');
      const matches = current().candidates.filter(
        (candidate) => status === null || candidate.status === status,
      );

      return HttpResponse.json({ items: matches, nextCursor: null, totalCount: matches.length });
    }),

    http.post('/api/v1/discovery/candidates/:id/promote', async ({ params, request }) => {
      const state = current();
      const body = await request.json();

      state.writes.push({
        method: 'POST',
        path: `/discovery/candidates/${String(params['id'])}/promote`,
        body,
      });

      const created = makeDetail({
        id: '019226b4-1000-7000-8000-000000000099',
        hostname: (body as { hostname: string }).hostname,
        vendor: 'Unknown',
        state: 'Unknown',
      });

      state.detail.set(created.id, created);
      state.candidates = state.candidates.filter(
        (candidate) => candidate.id !== String(params['id']),
      );

      return HttpResponse.json(created, { status: 201 });
    }),

    http.post('/api/v1/discovery/candidates/:id/ignore', ({ params }) => {
      const state = current();

      state.writes.push({
        method: 'POST',
        path: `/discovery/candidates/${String(params['id'])}/ignore`,
        body: null,
      });
      state.candidates = state.candidates.filter(
        (candidate) => candidate.id !== String(params['id']),
      );

      return new HttpResponse(null, { status: 204 });
    }),

    http.get('/api/v1/discovery/runs', () =>
      HttpResponse.json({
        items: current().runs,
        nextCursor: null,
        totalCount: current().runs.length,
      }),
    ),

    http.get('/api/v1/discovery/runs/:id', ({ params }) => {
      const run = current().runDetail.get(String(params['id']));

      return run === undefined ? notFound('discovery.run-not-found') : HttpResponse.json(run);
    }),

    http.get('/api/v1/discovery/runs/:id/hosts', ({ params, request }) => {
      const outcome = new URL(request.url).searchParams.get('outcome');
      const hosts = (current().runHosts.get(String(params['id'])) ?? []).filter(
        (host) => outcome === null || host.outcome === outcome,
      );

      return HttpResponse.json({ items: hosts, nextCursor: null, totalCount: hosts.length });
    }),

    http.post('/api/v1/discovery/runs', ({ request }) => {
      const state = current();
      const seedId = new URL(request.url).searchParams.get('seedId') ?? '';

      state.writes.push({ method: 'POST', path: '/discovery/runs', body: { seedId } });

      return HttpResponse.json(
        {
          runId: '019226b4-4000-7000-8000-000000000009',
          seedId,
          jobCount: 1,
          addressCount: 254,
          queuedAt: at,
        },
        { status: 202 },
      );
    }),

    http.get('/api/v1/discovery/seeds', () =>
      HttpResponse.json({
        items: current().seeds,
        nextCursor: null,
        totalCount: current().seeds.length,
      }),
    ),

    http.get('/api/v1/discovery/seeds/:id', ({ params }) => {
      const seed = current().seeds.find((candidate) => candidate.id === String(params['id']));

      return seed === undefined
        ? notFound('discovery.seed-not-found')
        : HttpResponse.json({
            ...seed,
            description: null,
            ranges: ['10.0.0.0/24'],
            exclusions: [],
            createdAt: at,
          });
    }),

    http.post('/api/v1/discovery/seeds', async ({ request }) => {
      const state = current();
      const body = await request.json();

      state.writes.push({ method: 'POST', path: '/discovery/seeds', body });

      const sent = body as { name: string; intervalMinutes: number };
      const created = makeSeed({
        id: `019226b4-5000-7000-8000-00000000${(state.seeds.length + 10).toString()}`,
        name: sent.name,
        intervalMinutes: sent.intervalMinutes,
      });

      state.seeds.push(created);

      return HttpResponse.json(
        { ...created, description: null, ranges: [], exclusions: [], createdAt: at },
        { status: 201 },
      );
    }),

    http.put('/api/v1/discovery/seeds/:id', async ({ params, request }) => {
      const state = current();
      const body = await request.json();

      state.writes.push({ method: 'PUT', path: `/discovery/seeds/${String(params['id'])}`, body });

      return HttpResponse.json({
        ...makeSeed({ id: String(params['id']), name: (body as { name: string }).name }),
        description: null,
        ranges: [],
        exclusions: [],
        createdAt: at,
      });
    }),

    http.delete('/api/v1/discovery/seeds/:id', ({ params }) => {
      const state = current();

      state.writes.push({
        method: 'DELETE',
        path: `/discovery/seeds/${String(params['id'])}`,
        body: null,
      });
      state.seeds = state.seeds.filter((seed) => seed.id !== String(params['id']));

      return new HttpResponse(null, { status: 204 });
    }),

    http.get('/api/v1/discovery/ignores', () =>
      HttpResponse.json({
        items: current().ignores,
        nextCursor: null,
        totalCount: current().ignores.length,
      }),
    ),

    http.post('/api/v1/discovery/ignores', async ({ request }) => {
      const state = current();
      const body = await request.json();

      state.writes.push({ method: 'POST', path: '/discovery/ignores', body });

      const sent = body as { cidr: string; reason: string | null };
      const created: DiscoveryIgnoreEntry = {
        id: `019226b4-7000-7000-8000-00000000${(state.ignores.length + 10).toString()}`,
        cidr: sent.cidr,
        reason: sent.reason,
        createdAt: at,
      };

      state.ignores.push(created);

      return HttpResponse.json(created, { status: 201 });
    }),

    http.delete('/api/v1/discovery/ignores/:id', ({ params }) => {
      const state = current();

      state.writes.push({
        method: 'DELETE',
        path: `/discovery/ignores/${String(params['id'])}`,
        body: null,
      });
      state.ignores = state.ignores.filter((entry) => entry.id !== String(params['id']));

      return new HttpResponse(null, { status: 204 });
    }),
  ];
}

function notFound(code: string) {
  return HttpResponse.json({ status: 404, title: 'Not found', code }, { status: 404 });
}

function equals(wanted: string | null, actual: string): boolean {
  return wanted === null || wanted === actual;
}

function equalsText(wanted: string | null, actual: string | null): boolean {
  return wanted === null || wanted.toLowerCase() === (actual ?? '').toLowerCase();
}

function contains(wanted: string | null, actual: string): boolean {
  return wanted === null || actual.toLowerCase().includes(wanted.toLowerCase());
}
