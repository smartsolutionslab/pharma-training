import { fileURLToPath, URL } from 'node:url';
import vue from '@vitejs/plugin-vue';
import { defineConfig } from 'vitest/config';
import tailwindcss from '@tailwindcss/vite';

// Under Aspire the host's address arrives as services__host__http__0; /api is forwarded there without the prefix.
const host = process.env.services__host__http__0;

export default defineConfig({
  // vue-i18n's feature flags: the Composition API only, without the legacy API or devtools in production.
  define: {
    __VUE_I18N_FULL_INSTALL__: true,
    __VUE_I18N_LEGACY_API__: false,
    __INTLIFY_PROD_DEVTOOLS__: false,
  },
  plugins: [
    // Vue's first, then the chosen packs' in order.
    vue(),
    tailwindcss(),
  ],
  // Every start scans the app's imports again and pre-bundles all of them before a page loads. Vite would otherwise reuse
  // node_modules/.vite while the lockfile and this file are unchanged, whatever the app imports now, and bundle the
  // rest as a page asks for it: the page then reloads, and its requests for the bundles it had answer 504.
  optimizeDeps: { force: true },
  resolve: { alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) } },
  server: host
    ? { proxy: { '/api': { target: host, changeOrigin: true, rewrite: (path) => path.replace(/^\/api/, '') } } }
    : {},
  test: { environment: 'jsdom', include: ['src/**/*.test.ts'], setupFiles: ['./src/test-setup.ts'] },
});
