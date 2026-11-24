import { expect, test, vi } from 'vitest';
import { loadPdfJs } from './pdfJs';

// pdf.js 6's modern build calls JavaScript APIs that only the newest engines have (Map.prototype.getOrInsertComputed,
// Math.sumPrecise, Uint8Array.fromBase64), without polyfills: iPad Safari and older browsers fail on it. The chapter
// player loads pdfjs-dist's legacy build, with the legacy worker, which carries them.
const legacy = vi.hoisted(() => ({
  getDocument: vi.fn(),
  GlobalWorkerOptions: { workerSrc: '' },
  TextLayer: vi.fn(),
}));
vi.mock('pdfjs-dist/legacy/build/pdf.mjs', () => legacy);

test("pdf.js is pdfjs-dist's legacy build, with its legacy worker", async () => {
  const pdfJs = await loadPdfJs();

  expect(pdfJs.getDocument).toBe(legacy.getDocument);
  expect(pdfJs.TextLayer).toBe(legacy.TextLayer);
  expect(legacy.GlobalWorkerOptions.workerSrc).toMatch(/pdfjs-dist\/legacy\/build\/pdf\.worker(\.min)?\.mjs/);
});
