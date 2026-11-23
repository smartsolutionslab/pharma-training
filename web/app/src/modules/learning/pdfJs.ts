import type { getDocument } from 'pdfjs-dist';

/** What getDocument takes, as pdfjs-dist types it (the type itself is not exported). */
type DocumentParameters = NonNullable<Parameters<typeof getDocument>[0]>;

/**
 * The parameters a PDF chapter opens its file with. Eval stays off: older pdf.js compiled font programs with eval
 * unless `isEvalSupported` was false; the pdf.js bundled here has no such path and ignores the flag, which is kept so a
 * different pdf.js could never turn it on. The file is read in one request rather than in ranges, so a page turned after
 * the signed URL expired still has its data.
 */
export function documentParameters(url: string): DocumentParameters & { isEvalSupported: false } {
  return { url, isEvalSupported: false, disableRange: true };
}

/**
 * pdf.js, loaded the first time a PDF chapter opens: it is large, and most chapters are no PDF. Its worker is bundled
 * with the app (Vite's `?url`) and served from the app's own origin.
 */
export async function loadPdfJs() {
  const [pdfJs, worker] = await Promise.all([import('pdfjs-dist'), import('pdfjs-dist/build/pdf.worker.min.mjs?url')]);
  pdfJs.GlobalWorkerOptions.workerSrc = worker.default;
  return pdfJs;
}
