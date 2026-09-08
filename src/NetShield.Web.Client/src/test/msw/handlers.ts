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

/** Resets the API to a signed-in administrator, optionally with something changed. */
export function resetApi(overrides: Partial<TestApiState> = {}): TestApiState {
  api = createTestApi(overrides);
  inventory = createInventoryApi();

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
export const handlers = [...authHandlers(() => api), ...inventoryHandlers(() => inventory)];

export { readOnlyUser, testUser } from '@/test/msw/testApi';
