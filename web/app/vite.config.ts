import { readdirSync, readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { dirname, join } from 'node:path';
import { fileURLToPath, URL } from 'node:url';
import vue from '@vitejs/plugin-vue';
import { defineConfig, type Plugin } from 'vitest/config';
import tailwindcss from '@tailwindcss/vite';

/**
 * pdf.js's own files that the PDF chapter (#44) points it at, served under /pdfjs/ by the dev server and copied there
 * by the build, with their names as pdf.js asks for them: wasm/ holds the decoders of JPX (OpenJPEG) and JBIG2 images
 * and the colour profiles (QCMS), with their fallbacks for a browser without WebAssembly; standard_fonts/ holds the
 * fonts of a PDF that does not embed its own. QuickJS, pdf.js's scripting, stays out: scripting is never on. pdf.js
 * fetches each only when a document needs it, so none of them is in the app's bundle.
 */
function pdfJsAssets(): Plugin {
  const root = dirname(createRequire(import.meta.url).resolve('pdfjs-dist/package.json'));
  const shipped = (directory: string) =>
    readdirSync(join(root, directory))
      .filter((name) => !/quickjs/i.test(name))
      .map((name) => ({ name, path: join(root, directory, name) }));
  const directories = ['wasm', 'standard_fonts'];
  const contentTypes: Record<string, string> = { '.wasm': 'application/wasm', '.js': 'text/javascript' };
  return {
    name: 'pdfjs-assets',
    configureServer(server) {
      server.middlewares.use('/pdfjs', (request, response, next) => {
        const [directory, name] = (request.url ?? '').replace(/^\//, '').split('?')[0]?.split('/') ?? [];
        const file = directories.includes(directory ?? '')
          ? shipped(directory ?? '').find((candidate) => candidate.name === name)
          : undefined;
        if (file === undefined) {
          next();
          return;
        }
        const extension = file.name.slice(file.name.lastIndexOf('.'));
        response.setHeader('Content-Type', contentTypes[extension] ?? 'application/octet-stream');
        response.end(readFileSync(file.path));
      });
    },
    generateBundle() {
      for (const directory of directories) {
        for (const file of shipped(directory)) {
          this.emitFile({
            type: 'asset',
            fileName: `pdfjs/${directory}/${file.name}`,
            source: readFileSync(file.path),
          });
        }
      }
    },
  };
}

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
    pdfJsAssets(),
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
