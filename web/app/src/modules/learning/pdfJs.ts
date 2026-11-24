import type { getDocument } from 'pdfjs-dist/legacy/build/pdf.mjs';

/** What getDocument takes, as pdfjs-dist types it (the type itself is not exported). */
type DocumentParameters = NonNullable<Parameters<typeof getDocument>[0]>;

/** Where the app serves pdf.js's own files (vite.config.ts, pdfJsAssets), each directory with its trailing slash. */
const assets = `${import.meta.env.BASE_URL}pdfjs/`;

/**
 * The parameters a PDF chapter opens its file with. The file is read in one request rather than in ranges, so a page
 * turned after the signed URL expired still has its data. XFA forms are not rendered, and scripting needs a viewer's
 * scripting manager that is never set up here. The wasm decoders (JPX and JBIG2 images) and the standard fonts (for a
 * PDF that does not embed its fonts) come from the app's own origin; without them such pages render blank.
 */
export function documentParameters(url: string): DocumentParameters {
  return {
    url,
    disableRange: true,
    enableXfa: false,
    wasmUrl: `${assets}wasm/`,
    standardFontDataUrl: `${assets}standard_fonts/`,
  };
}

/**
 * pdf.js, loaded the first time a PDF chapter opens: it is large, and most chapters are no PDF. It is pdfjs-dist's
 * legacy build: the modern one calls JavaScript APIs only the newest engines have (Map.prototype.getOrInsertComputed,
 * Math.sumPrecise, Uint8Array.fromBase64), without polyfills, and fails on iPad Safari and older browsers. Its worker,
 * the legacy one too, is bundled with the app (Vite's `?url`) and served from the app's own origin.
 */
export async function loadPdfJs() {
  const [pdfJs, worker] = await Promise.all([
    import('pdfjs-dist/legacy/build/pdf.mjs'),
    import('pdfjs-dist/legacy/build/pdf.worker.min.mjs?url'),
  ]);
  pdfJs.GlobalWorkerOptions.workerSrc = worker.default;
  return pdfJs;
}
