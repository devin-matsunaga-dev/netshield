/// <reference types="vitest/config" />
import { fileURLToPath, URL } from 'node:url';

import tailwindcss from '@tailwindcss/vite';
import { tanstackRouter } from '@tanstack/router-plugin/vite';
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

/**
 * Where the API is, in development. `aspire run` supplies it; SPEC.md §5 keeps the address out of
 * the repository, so there is no fallback host here. Without it the dev server simply serves the
 * SPA and nothing proxies — which is the correct behaviour for a run that has no API.
 *
 * `NETSHIELD_API_URL` is the one that arrives, and the AppHost sets it for exactly this reason:
 * `npm run dev` executes its script through `sh -c`, and a POSIX shell exports only names that
 * are valid shell identifiers — so Aspire's own `services__web-host__http__0` is dropped on the
 * way here, in silence, because of the hyphen in the resource name. It is still read first, for
 * a launcher that does not go through a shell.
 */
const apiUrl = process.env['services__web-host__http__0'] ?? process.env['NETSHIELD_API_URL'];

export default defineConfig({
  plugins: [tanstackRouter({ target: 'react', autoCodeSplitting: true }), react(), tailwindcss()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  build: {
    // Straight into the host's wwwroot. Nothing copies the build afterwards, so nothing can
    // copy a stale one (ARCHITECTURE.md §2 — Web.Host serves the SPA).
    outDir: '../NetShield.Web.Host/wwwroot',
    emptyOutDir: true,
  },
  server: apiUrl ? { proxy: { '/api': { target: apiUrl, changeOrigin: false } } } : {},
  test: {
    environment: 'jsdom',
    globals: false,
    setupFiles: ['./src/test/setup.ts'],
    include: ['src/**/*.test.{ts,tsx}'],
    restoreMocks: true,
  },
});
