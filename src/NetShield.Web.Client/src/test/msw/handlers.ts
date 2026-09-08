import { clientHandlers, createClientsApi, type ClientsApiState } from '@/test/msw/clientsApi';
import {
  createInventoryApi,
  inventoryHandlers,
  type InventoryApiState,
} from '@/test/msw/inventoryApi';
import { authHandlers, createTestApi, type TestApiState } from '@/test/msw/testApi';

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

/** Resets the API to a signed-in administrator, optionally with something changed. */
export function resetApi(overrides: Partial<TestApiState> = {}): TestApiState {
  api = createTestApi(overrides);
  inventory = createInventoryApi();
  clients = createClientsApi();

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

export const handlers = [
  ...authHandlers(() => api),
  ...inventoryHandlers(() => inventory),
  ...clientHandlers(() => clients),
];

export { readOnlyUser, testUser } from '@/test/msw/testApi';
