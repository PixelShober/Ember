import type { Plugin } from 'vite';
import { defineConfig } from 'vitest/config';
import { svelte } from '@sveltejs/vite-plugin-svelte';

// Strict Content-Security-Policy for the packaged UI. Not used for the dev server,
// because Vite's hot reload needs inline scripts and a websocket.
const csp: Plugin = {
  name: 'ember-csp',
  apply: 'build',
  transformIndexHtml(html) {
    const policy = [
      "default-src 'self'",
      "script-src 'self'",
      "style-src 'self' 'unsafe-inline'",
      "img-src 'self' data:",
      "font-src 'self'",
      "connect-src 'self'",
      "object-src 'none'",
      "base-uri 'none'",
      "form-action 'none'",
    ].join('; ');
    return html.replace('<meta charset="utf-8">', `<meta charset="utf-8">\n    <meta http-equiv="Content-Security-Policy" content="${policy}">`);
  },
};

export default defineConfig({
  // Relative paths: the UI is served from https://ui.ember.invalid/ or any folder.
  base: './',
  plugins: [svelte(), csp],
  build: {
    target: 'es2022',
    outDir: 'dist',
    emptyOutDir: true,
    sourcemap: false,
    chunkSizeWarningLimit: 800,
  },
  server: { port: 5173, strictPort: true },
  test: {
    include: ['src/**/*.test.ts'],
  },
});
