import { createRequire } from 'node:module';
import type { Page } from '@playwright/test';
import type jsQR from 'jsqr';
import type * as PdfJs from 'pdfjs-dist/legacy/build/pdf.mjs';

// Reading a certificate's PDF (CertificatePdfRenderer) as its holder would: the link its QR code encodes, decoded from
// the image, and its text, in which the same link is printed below the code ("Echtheit prüfen: <link>").

/**
 * The link the PDF's QR code encodes, decoded from the image itself: CertificatePdfRenderer draws the code as vector
 * rectangles, so its first page is drawn on a canvas by pdf.js and the code found and decoded there by jsQR. Both run
 * in the browser page (left blank for it), which has a canvas; in Node pdf.js could only draw with @napi-rs/canvas,
 * which the workspace does not install. pdf.js's worker runs on the page's main thread (it sets pdfjsWorker), so no
 * worker file has to be served. Resolves to '' when no QR code is found.
 */
export async function qrLinkIn(page: Page, bytes: Buffer) {
  const resolve = createRequire(import.meta.url).resolve;
  await page.setContent('<!doctype html><title>QR</title>');
  await page.addScriptTag({ path: resolve('pdfjs-dist/legacy/build/pdf.worker.mjs'), type: 'module' });
  await page.addScriptTag({ path: resolve('pdfjs-dist/legacy/build/pdf.mjs'), type: 'module' });
  await page.addScriptTag({ path: resolve('jsqr') });
  await page.waitForFunction(() => ['pdfjsLib', 'pdfjsWorker', 'jsQR'].every((name) => name in globalThis));
  return page.evaluate(async (base64) => {
    const { pdfjsLib, jsQR: decode } = globalThis as unknown as { pdfjsLib: typeof PdfJs; jsQR: typeof jsQR };
    const data = Uint8Array.from(atob(base64), (character) => character.charCodeAt(0));
    const loading = pdfjsLib.getDocument({ data });
    try {
      const pdfPage = await (await loading.promise).getPage(1);
      // Three times the PDF's 72 dpi: a module of the 35 mm code is several pixels wide.
      const viewport = pdfPage.getViewport({ scale: 3 });
      const canvas = document.createElement('canvas');
      canvas.width = Math.ceil(viewport.width);
      canvas.height = Math.ceil(viewport.height);
      await pdfPage.render({ canvas, viewport }).promise;
      const context = canvas.getContext('2d');
      if (context === null) {
        throw new Error('The browser gave the canvas no 2D context.');
      }
      const image = context.getImageData(0, 0, canvas.width, canvas.height);
      return decode(image.data, image.width, image.height)?.data ?? '';
    } finally {
      await loading.destroy();
    }
  }, bytes.toString('base64'));
}

/**
 * The text of a PDF, a line per line pdf.js reports. pdf.js (the app's own dependency, for PDF chapters) is loaded
 * only here: loading it in Node warns that it cannot draw, which reading text never needs.
 */
export async function pdfText(bytes: Buffer) {
  const { getDocument } = await import('pdfjs-dist/legacy/build/pdf.mjs');
  const loading = getDocument({ data: new Uint8Array(bytes) });
  const document = await loading.promise;
  try {
    const lines: string[] = [];
    for (let number = 1; number <= document.numPages; number++) {
      const content = await (await document.getPage(number)).getTextContent();
      for (const item of content.items) {
        if ('str' in item) {
          lines.push(item.str, item.hasEOL ? '\n' : '');
        }
      }
      lines.push('\n');
    }
    return lines.join('');
  } finally {
    await loading.destroy();
  }
}
