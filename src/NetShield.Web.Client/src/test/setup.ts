import '@testing-library/jest-dom/vitest';

import { cleanup } from '@testing-library/react';
import { afterAll, afterEach, beforeAll, beforeEach, vi } from 'vitest';

import { server } from '@/test/msw/server';
import { installMatchMedia } from '@/test/viewport';

beforeAll(() => {
  vi.stubGlobal('scrollTo', vi.fn());

  server.listen({ onUnhandledRequest: 'error' });
});

beforeEach(() => {
  // jsdom implements neither, and the shell asks for both.
  installMatchMedia();
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
