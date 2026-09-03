/// <reference types="vitest/config" />
import { fileURLToPath, URL } from 'node:url';

import tailwindcss from '@tailwindcss/vite';
import { tanstackRouter } from '@tanstack/router-plugin/vite';
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

/**
 * Where the API is, in development. Aspire publishes it as a service-discovery variable when
 * `aspire run` starts this app alongside NetShield.Web.Host; SPEC.md §5 keeps the address out of
 * the repository, so there is no fallback host here. Without it the dev server simply serves the
 * SPA and nothing proxies — which is the correct behaviour for a run that has no API.
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
