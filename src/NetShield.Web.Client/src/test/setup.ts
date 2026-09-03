import '@testing-library/jest-dom/vitest';

import { cleanup } from '@testing-library/react';
import { afterAll, afterEach, beforeAll, beforeEach, vi } from 'vitest';

import { resetSilentRefresh } from '@/features/session/api/silentRefresh';
import { resetApi } from '@/test/msw/handlers';
import { server } from '@/test/msw/server';
import { installMatchMedia } from '@/test/viewport';

beforeAll(() => {
  vi.stubGlobal('scrollTo', vi.fn());

  server.listen({ onUnhandledRequest: 'error' });
});

beforeEach(() => {
  // jsdom implements neither, and the shell asks for both.
  installMatchMedia();

  // A signed-in administrator, and no refresh left half-finished by the last test. The single
  // -flight latch in silentRefresh is module state, and module state outlives a test.
  resetApi();
  resetSilentRefresh();
});

afterEach(() => {
  cleanup();
  server.resetHandlers();
  window.localStorage.clear();
  document.documentElement.removeAttribute('data-theme');
});

afterAll(() => {
  server.close();
});
