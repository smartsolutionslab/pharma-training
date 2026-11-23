import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { render, screen, waitFor } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import { createMemoryHistory } from 'vue-router';
import type { LearningMediaUrlDto } from '@/api/generated';
import { createAppRouter } from '@/router';
import {
  type ChapterProgress,
  courseVersion,
  handout,
  introduction,
  session,
  sessionId,
  versionId,
} from './coursePlayerFixtures';
import CoursePlayerPage from './CoursePlayerPage.vue';
import { FakeIntersectionObserver } from './fakeIntersectionObserver';
import { type FakeLoadingTask, fakeDocument } from './fakePdf';

// The page shows a PDF chapter with PdfChapter (#44) and reports it through useProgressReporter: the last page shown
// reports 100 at once. pdfjs-dist is stubbed at its import.
const pdfjs = vi.hoisted(() => ({
  getDocument: vi.fn<(parameters: Record<string, unknown>) => FakeLoadingTask>(),
  GlobalWorkerOptions: { workerSrc: '' },
}));
vi.mock('pdfjs-dist', () => pdfjs);
vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockImplementation(
  () => ({}) as unknown as CanvasRenderingContext2D,
);
FakeIntersectionObserver.install();
const host = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>();
vi.stubGlobal('fetch', host);

const chapters = [introduction, handout];
let progress: Record<string, ChapterProgress>;

beforeEach(() => {
  FakeIntersectionObserver.reset();
  progress = {};
  const { document } = fakeDocument(2);
  pdfjs.getDocument.mockReset();
  pdfjs.getDocument.mockImplementation(() => ({
    promise: Promise.resolve(document),
    destroy: async () => undefined,
  }));
  host.mockImplementation(async (input, init) => {
    const url = new URL(String(input), 'http://localhost');
    const method = init?.method ?? 'GET';
    if (method === 'GET' && url.pathname === `/api/catalog/course-versions/${versionId}`) {
      return Response.json(courseVersion(chapters));
    }
    if (method === 'POST' && url.pathname === '/api/learning/sessions') {
      return Response.json(session(progress, 'InProgress', chapters), { status: 201 });
    }
    if (method === 'GET' && url.pathname === `/api/learning/sessions/${sessionId}`) {
      return Response.json(session(progress, 'InProgress', chapters));
    }
    if (method === 'GET' && url.pathname === `/api/learning/media/${handout.chapterId}`) {
      const answer: LearningMediaUrlDto = {
        url: 'https://storage.example/media/merkblatt.pdf?sig=page',
        expiresAt: new Date(Date.now() + 15 * 60_000).toISOString(),
        contentType: 'application/pdf',
        kind: 'Pdf',
      };
      return Response.json(answer);
    }
    if (method === 'POST' && url.pathname === `/api/learning/sessions/${sessionId}/progress`) {
      const body = JSON.parse(String(init?.body)) as { chapterId: string; percent: number };
      progress = { ...progress, [body.chapterId]: { percent: body.percent, completed: body.percent === 100 } };
      return Response.json(session(progress, 'InProgress', chapters));
    }
    return new Response('', { status: 404 });
  });
});
afterEach(() => host.mockReset());

const reports = () =>
  host.mock.calls
    .filter(([input]) => String(input).endsWith('/progress'))
    .map(([, init]) => JSON.parse(String(init?.body)) as unknown);

test('the PDF chapter is shown in the page; its last page reports 100, and the chapter is marked done', async () => {
  const router = createAppRouter(createMemoryHistory());
  await router.push(`/learning/courses/${versionId}?chapter=${handout.chapterId}`);
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  render(CoursePlayerPage, { global: { plugins: [router, [VueQueryPlugin, { queryClient }]] } });

  expect(await screen.findByLabelText(`PDF: ${handout.title}`)).toBeInTheDocument();
  await screen.findByText('Seite 1 von 2');
  await userEvent.click(screen.getByRole('button', { name: 'Nächste Seite' }));

  await waitFor(() =>
    expect(reports()).toEqual([
      { chapterId: handout.chapterId, percent: 50 },
      { chapterId: handout.chapterId, percent: 100 },
    ]),
  );
  const chapterHeading = screen.getByRole('heading', { level: 2, name: handout.title });
  await waitFor(() => expect(chapterHeading.parentElement).toHaveTextContent('Erledigt'));
});
