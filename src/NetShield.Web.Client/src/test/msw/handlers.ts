import { clientHandlers, createClientsApi, type ClientsApiState } from '@/test/msw/clientsApi';
import {
  createCredentialsApi,
  credentialHandlers,
  type CredentialsApiState,
} from '@/test/msw/credentialsApi';
import {
  createInventoryApi,
  inventoryHandlers,
  type InventoryApiState,
} from '@/test/msw/inventoryApi';
import { authHandlers, createTestApi, type TestApiState } from '@/test/msw/testApi';
import { createTopologyApi, topologyHandlers, type TopologyApiState } from '@/test/msw/topologyApi';

/**
 * The API a test sees. `resetApi` replaces it before every test, and a test may go on mutating
 * it — expire the session, refuse the refresh, sign in a read-only user — to change what the API
 * does mid-run.
 */
export let api: TestApiState = createTestApi();

/**
 * The inventory the SPA sees. Separate from the session state, because a test about the device
 * table has nothing to say about who is signed in and vice versa.
 */
export let inventory: InventoryApiState = createInventoryApi();

/**
 * The clients the SPA sees. Separate from the inventory for the same reason the inventory is
 * separate from the session: a test about the client table has nothing to say about devices.
 */
export let clients: ClientsApiState = createClientsApi();

/** The credential profiles the SPA sees. Its own state, for the reason the clients have theirs. */
export let credentials: CredentialsApiState = createCredentialsApi();

/**
 * The topology graph the SPA sees. Its own state, for the reason the clients have theirs: a test
 * about the map has nothing to say about the device table, and the map is a whole graph rather
 * than a list of devices.
 */
export let topology: TopologyApiState = createTopologyApi();

/** Resets the API to a signed-in administrator, optionally with something changed. */
export function resetApi(overrides: Partial<TestApiState> = {}): TestApiState {
  api = createTestApi(overrides);
  inventory = createInventoryApi();
  clients = createClientsApi();
  credentials = createCredentialsApi();
  topology = createTopologyApi();

  return api;
}

/** Replaces the inventory a test sees. Called after `resetApi`, which empties it. */
export function setInventory(overrides: Partial<InventoryApiState> = {}): InventoryApiState {
  inventory = createInventoryApi(overrides);

  return inventory;
}

/**
 * The default API. Every handler answers the shape `src/api/schema.d.ts` describes, so a fixture
 * that drifts from the contract fails to type-check rather than passing a test that lies. The
 * handlers read `api` when they are called, so replacing it needs no word to MSW.
 */
/** Replaces the clients a test sees. Called after `resetApi`, which empties them. */
export function setClients(overrides: Partial<ClientsApiState> = {}): ClientsApiState {
  clients = createClientsApi(overrides);

  return clients;
}

/** Replaces the credential profiles a test sees. Called after `resetApi`, which empties them. */
export function setCredentials(overrides: Partial<CredentialsApiState> = {}): CredentialsApiState {
  credentials = createCredentialsApi(overrides);

  return credentials;
}

/** Replaces the topology graph a test sees. Called after `resetApi`, which empties it. */
export function setTopology(overrides: Partial<TopologyApiState> = {}): TopologyApiState {
  topology = createTopologyApi(overrides);

  return topology;
}

export const handlers = [
  ...authHandlers(() => api),
  // Before the inventory handlers: `/api/v1/credential-profiles` is claimed by both files
  // otherwise, and the inventory one only lists.
  ...credentialHandlers(() => credentials),
  ...inventoryHandlers(
    () => inventory,
    () => credentials.profiles,
  ),
  ...clientHandlers(() => clients),
  ...topologyHandlers(() => topology),
];

export { readOnlyUser, testUser } from '@/test/msw/testApi';
