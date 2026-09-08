import { http, HttpResponse, type RequestHandler } from 'msw';

import type { Schemas } from '@/api/types';

type ClientSummary = Schemas['ClientSummary'];
type ClientDetail = Schemas['ClientDetail'];
type ClientIpBindingSummary = Schemas['ClientIpBindingSummary'];
type ClientPortBindingSummary = Schemas['ClientPortBindingSummary'];
type AssetResolution = Schemas['AssetResolution'];

/**
 * The clients API a test sees.
 *
 * Its own module rather than a wing of `inventoryApi`, for the reason the session state is its
 * own: a test about the client table has nothing to say about devices, and a fixture file that
 * holds everything is one every test has to read past.
 *
 * Every shape here is the generated one, so a fixture that drifts from the contract fails to
 * type-check rather than passing a test that lies about what the API sends. Numeric members are
 * written as plain numbers, which is what the API actually writes — the contract's
 * `number | string` is the document describing what it will *accept*.
 */
export interface ClientsApiState {
  clients: ClientSummary[];
  detail: Map<string, ClientDetail>;
  ipHistory: Map<string, ClientIpBindingSummary[]>;
  portHistory: Map<string, ClientPortBindingSummary[]>;
  /** What `GET /clients/resolve` answers, keyed by the address asked about. */
  resolutions: Map<string, AssetResolution>;
  /** Turn the list into a 500, to reach the error state (DESIGN.md §8). */
  failClientList: boolean;
}

const at = '2026-09-08T09:00:00.000Z';

export const laptopId = '019226b4-2000-7000-8000-000000000001';
export const printerId = '019226b4-2000-7000-8000-000000000002';
export const switchId = '019226b4-1000-7000-8000-000000000001';

export function makeClient(overrides: Partial<ClientSummary> = {}): ClientSummary {
  return {
    id: laptopId,
    macAddress: 'AA:BB:CC:00:00:21',
    oui: 'AA:BB:CC',
    locallyAdministered: true,
    hostname: null,
    ipAddress: '10.10.0.21',
    deviceId: switchId,
    deviceHostname: 'core-sw-01',
    ifIndex: 1,
    vlanId: 10,
    firstSeenAt: at,
    lastSeenAt: at,
    ...overrides,
  };
}

export function makeClientDetail(overrides: Partial<ClientDetail> = {}): ClientDetail {
  return {
    id: laptopId,
    macAddress: 'AA:BB:CC:00:00:21',
    oui: 'AA:BB:CC',
    locallyAdministered: false,
    hostname: null,
    ipBindings: [makeIpBinding()],
    portBindings: [makePortBinding()],
    firstSeenAt: at,
    lastSeenAt: at,
    ...overrides,
  };
}

export function makeIpBinding(
  overrides: Partial<ClientIpBindingSummary> = {},
): ClientIpBindingSummary {
  return {
    id: '019226b4-3000-7000-8000-000000000001',
    ipAddress: '10.10.0.21',
    source: 'ArpTable',
    deviceId: switchId,
    deviceHostname: 'core-sw-01',
    observedFrom: at,
    observedTo: null,
    lastSeenAt: at,
    ...overrides,
  };
}

export function makePortBinding(
  overrides: Partial<ClientPortBindingSummary> = {},
): ClientPortBindingSummary {
  return {
    id: '019226b4-4000-7000-8000-000000000001',
    deviceId: switchId,
    deviceHostname: 'core-sw-01',
    ifIndex: 1,
    interfaceName: 'Gi1/0/1',
    vlanId: 10,
    macCountOnPort: 1,
    source: 'MacAddressTable',
    observedFrom: at,
    observedTo: null,
    lastSeenAt: at,
    ...overrides,
  };
}

export function makeResolution(overrides: Partial<AssetResolution> = {}): AssetResolution {
  return {
    ipAddress: '10.10.0.21',
    at,
    kind: 'Client',
    deviceId: null,
    deviceHostname: null,
    clientId: laptopId,
    macAddress: 'AA:BB:CC:00:00:21',
    observedFrom: at,
    observedTo: null,
    ...overrides,
  };
}

export function createClientsApi(overrides: Partial<ClientsApiState> = {}): ClientsApiState {
  return {
    clients: [],
    detail: new Map(),
    ipHistory: new Map(),
    portHistory: new Map(),
    resolutions: new Map(),
    failClientList: false,
    ...overrides,
  };
}

/**
 * The handlers, reading whatever `current()` returns so a test can replace the whole state
 * between runs without re-registering anything.
 *
 * The list applies the filters it is given, because "filters compose and survive a refresh" is
 * the criterion these screens are built to and a handler that ignored them would let a broken
 * filter pass. `resolve` refuses a value that is not an address with a 400, which is what the
 * real endpoint does and what the panel renders as a sentence rather than an error state.
 */
export function clientHandlers(current: () => ClientsApiState): RequestHandler[] {
  return [
    http.get('/api/v1/clients/resolve', ({ request }) => {
      const query = new URL(request.url).searchParams;
      const ipAddress = query.get('ipAddress') ?? '';
      const resolution = current().resolutions.get(ipAddress);

      if (resolution === undefined) {
        return HttpResponse.json(
          { status: 400, title: 'Bad request', code: 'client.invalid-address' },
          { status: 400 },
        );
      }

      return HttpResponse.json(resolution);
    }),

    http.get('/api/v1/clients', ({ request }) => {
      const state = current();

      if (state.failClientList) {
        return HttpResponse.json({ status: 500, title: 'Server error' }, { status: 500 });
      }

      const query = new URL(request.url).searchParams;

      const matches = state.clients.filter(
        (client) =>
          matchesSearch(query.get('search'), client) &&
          equalsNumber(query.get('vlanId'), client.vlanId) &&
          equalsText(query.get('deviceId'), client.deviceId) &&
          (query.get('onlyActive') !== 'true' || client.ipAddress !== null),
      );

      return HttpResponse.json({
        items: matches,
        nextCursor: null,
        totalCount: matches.length,
      });
    }),

    http.get('/api/v1/clients/:id', ({ params }) => {
      const client = current().detail.get(String(params['id']));

      return client === undefined ? notFound('client.not-found') : HttpResponse.json(client);
    }),

    http.get('/api/v1/clients/:id/ip-history', ({ params }) => {
      const items = current().ipHistory.get(String(params['id'])) ?? [];

      return HttpResponse.json({ items, nextCursor: null, totalCount: items.length });
    }),

    http.get('/api/v1/clients/:id/port-history', ({ params }) => {
      const items = current().portHistory.get(String(params['id'])) ?? [];

      return HttpResponse.json({ items, nextCursor: null, totalCount: items.length });
    }),
  ];
}

function notFound(code: string) {
  return HttpResponse.json({ status: 404, title: 'Not found', code }, { status: 404 });
}

/**
 * The one search box over three kinds of value, the way the server decides it: a MAC in any
 * spelling matches exactly, an address matches exactly, anything else is a hostname prefix.
 */
function matchesSearch(wanted: string | null, client: ClientSummary): boolean {
  if (wanted === null) {
    return true;
  }

  const normalised = wanted.replace(/[^0-9a-f]/gi, '').toUpperCase();

  if (normalised.length === 12) {
    return client.macAddress.replace(/:/g, '') === normalised;
  }

  return (
    client.ipAddress === wanted ||
    (client.hostname ?? '').toLowerCase().startsWith(wanted.toLowerCase())
  );
}

function equalsText(wanted: string | null, actual: string | null): boolean {
  return wanted === null || wanted === actual;
}

function equalsNumber(wanted: string | null, actual: number | string | null): boolean {
  return wanted === null || wanted === String(actual);
}
