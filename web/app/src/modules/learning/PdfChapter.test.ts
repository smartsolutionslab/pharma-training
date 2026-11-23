import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { render, screen, waitFor } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import type { LearningMediaUrlDto } from '@/api/generated';
import { setLanguage } from '@/i18n';
import { handout } from './coursePlayerFixtures';
import { type FakeDocument, type FakeLoadingTask, fakeDocument } from './fakePdf';
import PdfChapter from './PdfChapter.vue';

// A PDF chapter (#44, Q1 decided: pdf.js in the app): the file the signed URL of GET /learning/media/{chapterId}
// (useGetLearningMediaChapterId) names, rendered page by page through pdfjs-dist with eval off, in a region labelled
// "PDF: {title}", with "Seite {page} von {pages}" and next and previous page buttons. The percent is the highest page
// shown over the pages, rounded down, emitted as it rises; the last page shown gives 100 at once. It opens at the
// highest page whose own percent is at most the stored one. The hint shows until the chapter is done.
//
// <PdfChapter :chapter-id="chapter.chapterId" :title="chapter.title" :progress-percent="chapter.progressPercent"
//   :completed="chapter.completed" @progress="(percent) => reporter.report(chapterId, percent)" />
//
// pdfjs-dist is stubbed at its import: getDocument answers the document the test gives it.
const pdfjs = vi.hoisted(() => ({
  getDocument: vi.fn<(parameters: Record<string, unknown>) => FakeLoadingTask>(),
  GlobalWorkerOptions: { workerSrc: '' },
}));
vi.mock('pdfjs-dist', () => pdfjs);

const host = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>();
vi.stubGlobal('fetch', host);
// jsdom draws nothing: a canvas hands out a context that ignores what is drawn.
vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockImplementation(
  () => ({}) as unknown as CanvasRenderingContext2D,
);

const signedUrl = 'https://storage.example/media/merkblatt.pdf?sig=pdf';

/** Gives getDocument the document to answer with. */
function serving(document: FakeDocument) {
  pdfjs.getDocument.mockImplementation(() => ({
    promise: Promise.resolve(document),
    destroy: async () => undefined,
  }));
}

beforeEach(() => {
  pdfjs.getDocument.mockReset();
  host.mockImplementation(async (input) => {
    const url = new URL(String(input), 'http://localhost');
    if (url.pathname === `/api/learning/media/${handout.chapterId}`) {
      const answer: LearningMediaUrlDto = {
        url: signedUrl,
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

test('the chapter loads the signed URL through pdf.js with eval off, and renders its first page', async () => {
  const { document, getPage, rendered } = fakeDocument(4);
  serving(document);
  renderChapter();

  expect(await screen.findByText('Seite 1 von 4')).toBeVisible();
  expect(pdfjs.getDocument).toHaveBeenCalledWith(expect.objectContaining({ url: signedUrl, isEvalSupported: false }));
  expect(pdfjs.GlobalWorkerOptions.workerSrc).not.toBe('');
  expect(getPage).toHaveBeenCalledWith(1);
  await waitFor(() => expect(rendered).toEqual([1]));
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
  expect(screen.getByText('Scroll to the last page to complete the chapter.')).toBeVisible();
});
