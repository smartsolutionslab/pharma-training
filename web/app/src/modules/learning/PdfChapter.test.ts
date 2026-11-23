import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { render, screen, waitFor, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import type { LearningMediaUrlDto } from '@/api/generated';
import { setLanguage } from '@/i18n';
import { handout } from './coursePlayerFixtures';
import { type FakeDocument, type FakeLoadingTask, fakeDocument, textOfPage } from './fakePdf';
import PdfChapter from './PdfChapter.vue';

// A PDF chapter (#44, Q1 decided: pdf.js in the app): the file the signed URL of GET /learning/media/{chapterId}
// (useGetLearningMediaChapterId) names, rendered page by page through pdfjs-dist's legacy build (which runs on older
// engines such as iPad Safari) with scripting and XFA off, in a region labelled "PDF: {title}", with the page's text
// laid over the canvas (pdf.js's TextLayer) for screen readers and selection, "Seite {page} von {pages}" in a polite
// live region, and next and previous page buttons. pdf.js's wasm decoders and standard fonts are served by the app.
// The percent is the highest page drawn over the pages, rounded down, emitted as it rises; the last page drawn gives
// 100 at once. It opens at the highest page whose own percent is at most the stored one. The hint shows until the
// chapter is done. A file that fails to open reads its signed URL again once (it may have expired), and so does Retry.
//
// <PdfChapter :chapter-id="chapter.chapterId" :title="chapter.title" :progress-percent="chapter.progressPercent"
//   :completed="chapter.completed" @progress="(percent) => reporter.report(chapterId, percent)" />
//
// pdfjs-dist's legacy build is stubbed at its import: getDocument answers the document the test gives it, and TextLayer
// puts a page's text into its container.
const pdfjs = vi.hoisted(() => ({
  getDocument: vi.fn<(parameters: Record<string, unknown>) => FakeLoadingTask>(),
  GlobalWorkerOptions: { workerSrc: '' },
}));
vi.mock('pdfjs-dist/legacy/build/pdf.mjs', async () => ({
  ...pdfjs,
  TextLayer: (await import('./fakePdf')).FakeTextLayer,
}));

const host = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>();
vi.stubGlobal('fetch', host);
// jsdom draws nothing: a canvas hands out a context that ignores what is drawn.
vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockImplementation(
  () => ({}) as unknown as CanvasRenderingContext2D,
);

const signedUrl = 'https://storage.example/media/merkblatt.pdf?sig=pdf';
const renewedUrl = 'https://storage.example/media/merkblatt.pdf?sig=renewed';
let issued: string;

/** Gives getDocument the document to answer with. */
function serving(document: FakeDocument) {
  pdfjs.getDocument.mockImplementation(() => ({
    promise: Promise.resolve(document),
    destroy: async () => undefined,
  }));
}

beforeEach(() => {
  pdfjs.getDocument.mockReset();
  issued = signedUrl;
  host.mockImplementation(async (input) => {
    const url = new URL(String(input), 'http://localhost');
    if (url.pathname === `/api/learning/media/${handout.chapterId}`) {
      const answer: LearningMediaUrlDto = {
        url: issued,
        expiresAt: new Date(Date.now() + 15 * 60_000).toISOString(),
        contentType: 'application/pdf',
        kind: 'Pdf',
      };
      return Response.json(answer);
    }
    return new Response('', { status: 404 });
  });
});
afterEach(() => host.mockReset());

function renderChapter(progress: { progressPercent?: number; completed?: boolean } = {}) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(PdfChapter, {
    props: {
      chapterId: handout.chapterId,
      title: handout.title,
      progressPercent: progress.progressPercent ?? 0,
      completed: progress.completed ?? false,
    },
    global: { plugins: [[VueQueryPlugin, { queryClient }]] },
  });
}

/** The percents the chapter emitted, in order. */
function progressOf(emitted: () => Record<string, unknown[][]>): unknown[] {
  return (emitted().progress ?? []).map(([percent]) => percent);
}

const next = () => screen.getByRole('button', { name: 'Nächste Seite' });
const previous = () => screen.getByRole('button', { name: 'Vorherige Seite' });

const mediaReads = () =>
  host.mock.calls.filter(([input]) => String(input).endsWith(`/learning/media/${handout.chapterId}`)).length;

test('the chapter loads the signed URL through pdf.js, and renders its first page', async () => {
  const { document, getPage, rendered } = fakeDocument(4);
  serving(document);
  renderChapter();

  expect(await screen.findByText('Seite 1 von 4')).toBeVisible();
  expect(pdfjs.getDocument).toHaveBeenCalledWith(expect.objectContaining({ url: signedUrl }));
  expect(pdfjs.GlobalWorkerOptions.workerSrc).not.toBe('');
  expect(getPage).toHaveBeenCalledWith(1);
  await waitFor(() => expect(rendered).toEqual([1]));
});

test('the file opens without scripting or XFA, with the wasm decoders and the standard fonts the app serves', async () => {
  serving(fakeDocument(1).document);
  renderChapter();
  await screen.findByText('Seite 1 von 1');

  const parameters = pdfjs.getDocument.mock.calls[0]?.[0] ?? {};
  // JPX and JBIG2 images need pdf.js's wasm decoders, and a PDF without embedded fonts its standard fonts: both are
  // directories of the app's own origin, given with their trailing slash as pdf.js wants them.
  expect(parameters.wasmUrl).toMatch(/\/pdfjs\/wasm\/$/);
  expect(parameters.standardFontDataUrl).toMatch(/\/pdfjs\/standard_fonts\/$/);
  // Scripting is a viewer's (PDFScriptingManager) and is never set up here; XFA forms are not rendered.
  expect(parameters).not.toHaveProperty('enableScripting');
  expect(parameters.enableXfa).toBeFalsy();
});

test("the page's text lies over the canvas, so screen readers read it and it can be selected", async () => {
  serving(fakeDocument(3).document);
  renderChapter();

  const region = await screen.findByLabelText(`PDF: ${handout.title}`);
  expect(await within(region).findByText(textOfPage(1))).toBeInTheDocument();
  // No annotation layer: links and forms of the file are not rendered as controls.
  expect(region.querySelector('.annotationLayer')).toBeNull();

  await userEvent.click(next());

  expect(await within(region).findByText(textOfPage(2))).toBeInTheDocument();
  expect(within(region).queryByText(textOfPage(1))).toBeNull();
});

test('the page indicator is a polite live region, so a page turned is heard', async () => {
  serving(fakeDocument(2).document);
  renderChapter();

  const indicator = await screen.findByText('Seite 1 von 2');

  expect(indicator).toHaveAttribute('aria-live', 'polite');
});

test('a page counts as shown once it is drawn, not before', async () => {
  const { document, draw } = fakeDocument(2, { holdDrawing: true });
  serving(document);
  const { emitted } = renderChapter();
  await screen.findByText('Seite 1 von 2');
  await waitFor(() => expect(pdfjs.getDocument).toHaveBeenCalled());

  expect(progressOf(emitted)).toEqual([]);
  draw(1);

  await waitFor(() => expect(progressOf(emitted)).toEqual([50]));
});

test('a file that fails to open reads its signed URL again and opens the new one, as after the URL expired', async () => {
  const { document } = fakeDocument(2);
  pdfjs.getDocument.mockImplementation(({ url }) => ({
    promise: url === signedUrl ? Promise.reject(new Error('403 from storage')) : Promise.resolve(document),
    destroy: async () => undefined,
  }));
  issued = signedUrl;
  renderChapter();
  await waitFor(() => expect(pdfjs.getDocument).toHaveBeenCalledWith(expect.objectContaining({ url: signedUrl })));
  issued = renewedUrl;

  expect(await screen.findByText('Seite 1 von 2')).toBeVisible();
  expect(pdfjs.getDocument).toHaveBeenLastCalledWith(expect.objectContaining({ url: renewedUrl }));
  expect(screen.queryByRole('alert')).toBeNull();
});

test('a file that fails again shows an alert, whose Retry reads the signed URL again', async () => {
  const { document } = fakeDocument(2);
  let opens = 0;
  pdfjs.getDocument.mockImplementation(() => {
    opens += 1;
    return {
      promise: opens <= 2 ? Promise.reject(new Error('403 from storage')) : Promise.resolve(document),
      destroy: async () => undefined,
    };
  });
  renderChapter();
  const alert = await screen.findByRole('alert');
  const readsBefore = mediaReads();

  await userEvent.click(within(alert).getByRole('button', { name: 'Erneut versuchen' }));

  expect(await screen.findByText('Seite 1 von 2')).toBeVisible();
  expect(mediaReads()).toBeGreaterThan(readsBefore);
});

test("the document is in a region labelled with the chapter's title", async () => {
  serving(fakeDocument(4).document);
  renderChapter();

  expect(await screen.findByLabelText(`PDF: ${handout.title}`)).toBeInTheDocument();
});

test('next and previous turn the pages; there is no page before the first or after the last', async () => {
  const { document, rendered } = fakeDocument(3);
  serving(document);
  renderChapter();
  await screen.findByText('Seite 1 von 3');
  expect(previous()).toBeDisabled();

  await userEvent.click(next());
  expect(await screen.findByText('Seite 2 von 3')).toBeVisible();
  await userEvent.click(next());
  expect(await screen.findByText('Seite 3 von 3')).toBeVisible();
  expect(next()).toBeDisabled();

  await userEvent.click(previous());
  expect(await screen.findByText('Seite 2 von 3')).toBeVisible();
  await waitFor(() => expect(rendered).toEqual([1, 2, 3, 2]));
});

test('the percent is the highest page shown over the pages, emitted as it rises; the last page gives 100', async () => {
  serving(fakeDocument(4).document);
  const { emitted } = renderChapter();
  await screen.findByText('Seite 1 von 4');
  await waitFor(() => expect(progressOf(emitted)).toEqual([25]));

  await userEvent.click(next());
  await screen.findByText('Seite 2 von 4');
  await userEvent.click(previous());
  await screen.findByText('Seite 1 von 4');
  await userEvent.click(next());
  await screen.findByText('Seite 2 von 4');
  await waitFor(() => expect(progressOf(emitted)).toEqual([25, 50]));

  await userEvent.click(next());
  await userEvent.click(next());

  await waitFor(() => expect(progressOf(emitted)).toEqual([25, 50, 75, 100]));
});

test('a PDF of one page is done as soon as it is shown', async () => {
  serving(fakeDocument(1).document);
  const { emitted } = renderChapter();

  await screen.findByText('Seite 1 von 1');
  await waitFor(() => expect(progressOf(emitted)).toEqual([100]));
});

test('the chapter opens at the page its stored percent stands for', async () => {
  // 66 % of 3 pages is page 2 (floor(2 / 3 × 100) = 66), not page 1.
  const { document, rendered } = fakeDocument(3);
  serving(document);
  renderChapter({ progressPercent: 66 });

  expect(await screen.findByText('Seite 2 von 3')).toBeVisible();
  await waitFor(() => expect(rendered).toEqual([2]));
});

test('the hint to page to the end shows until the chapter is done', async () => {
  serving(fakeDocument(2).document);
  const { rerender } = renderChapter();
  const hint = 'Blättern Sie bis zur letzten Seite, um das Kapitel abzuschließen.';

  expect(await screen.findByText(hint)).toBeVisible();
  await rerender({ chapterId: handout.chapterId, title: handout.title, progressPercent: 100, completed: true });

  expect(screen.queryByText(hint)).toBeNull();
});

test('the PDF chapter speaks English once switched', async () => {
  setLanguage('en-GB', { remember: false });
  serving(fakeDocument(2).document);
  renderChapter();

  expect(await screen.findByText('Page 1 of 2')).toBeVisible();
  expect(screen.getByLabelText(`PDF: ${handout.title}`)).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Next page' })).toBeEnabled();
  expect(screen.getByRole('button', { name: 'Previous page' })).toBeDisabled();
  expect(screen.getByText('Go to the last page to complete the chapter.')).toBeVisible();
});
