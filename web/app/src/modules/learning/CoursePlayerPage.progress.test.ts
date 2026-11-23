import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { cleanup, render, screen, waitFor, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, onTestFinished, test, vi } from 'vitest';
import { createMemoryHistory } from 'vue-router';
import type { LearningSessionDto } from '@/api/generated';
import { t } from '@/i18n';
import { createAppRouter } from '@/router';
import {
  type ChapterProgress,
  courseVersion,
  handwashing,
  introduction,
  refusal,
  session,
  sessionId,
  summary,
  versionId,
} from './coursePlayerFixtures';
import CoursePlayerPage from './CoursePlayerPage.vue';
import { FakeIntersectionObserver } from './fakeIntersectionObserver';

// The page reports a text chapter's progress through useProgressReporter (#44): 100 once the end is in view, and what
// the host answers is what the page shows. Announcements go to the page's polite status region; failures are alerts.
const host = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>();
vi.stubGlobal('fetch', host);
FakeIntersectionObserver.install();

let progress: Record<string, ChapterProgress>;
let status: LearningSessionDto['status'];
let reporting: (body: { chapterId: string; percent: number }) => Promise<Response>;

/** What the host stores for a report: the chapter at the percent, done at 100. */
async function storing({ chapterId, percent }: { chapterId: string; percent: number }) {
  progress = { ...progress, [chapterId]: { percent, completed: percent === 100 } };
  return Response.json(session(progress, status));
}

beforeEach(() => {
  FakeIntersectionObserver.reset();
  progress = {};
  status = 'InProgress';
  reporting = storing;
  host.mockImplementation(async (input, init) => {
    const url = new URL(String(input), 'http://localhost');
    const method = init?.method ?? 'GET';
    if (method === 'GET' && url.pathname === `/api/catalog/course-versions/${versionId}`) {
      return Response.json(courseVersion());
    }
    if (method === 'POST' && url.pathname === '/api/learning/sessions') {
      return Response.json(session(progress, status), { status: 201 });
    }
    if (method === 'GET' && url.pathname === `/api/learning/sessions/${sessionId}`) {
      return Response.json(session(progress, status));
    }
    if (method === 'POST' && url.pathname === `/api/learning/sessions/${sessionId}/progress`) {
      return reporting(JSON.parse(String(init?.body)));
    }
    return new Response('', { status: 404 });
  });
});
afterEach(() => host.mockReset());
// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(cleanup);

const reports = () =>
  host.mock.calls
    .filter(([input]) => String(input).endsWith('/progress'))
    .map(([, init]) => JSON.parse(String(init?.body)) as unknown);

/** The page on the introduction, a text chapter, with its end marker watched. */
async function renderPage() {
  const router = createAppRouter(createMemoryHistory());
  await router.push(`/learning/courses/${versionId}?chapter=${introduction.chapterId}`);
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  render(CoursePlayerPage, { global: { plugins: [router, [VueQueryPlugin, { queryClient }]] } });
  await screen.findByRole('heading', { level: 2, name: introduction.title });
  await waitFor(() => expect(FakeIntersectionObserver.observedCount()).toBeGreaterThan(0));
}

/** The status region (role="status") a text is in. */
const statusOf = (text: string) => screen.getByText(text).closest('[role="status"]');

test("a text chapter's end in view reports 100 for it, once", async () => {
  await renderPage();

  FakeIntersectionObserver.show();
  FakeIntersectionObserver.show();

  await waitFor(() => expect(reports()).toEqual([{ chapterId: introduction.chapterId, percent: 100 }]));
});

test('a text chapter that fits on the screen is done as soon as it is shown', async () => {
  await renderPage();

  // The browser calls back at once for an end marker already on the screen.
  FakeIntersectionObserver.show();

  await waitFor(() => expect(reports()).toEqual([{ chapterId: introduction.chapterId, percent: 100 }]));
});

test('a chapter done is announced politely, marked done beside its heading, and its hint goes', async () => {
  await renderPage();
  expect(screen.getByText(t('learning.player.text.hint'))).toBeInTheDocument();

  FakeIntersectionObserver.show();

  const announcement = t('learning.player.chapterDone', { title: introduction.title });
  await waitFor(() => expect(statusOf(announcement)).not.toBeNull());
  // The badge sits beside the h2, not in it: the heading keeps the chapter's title as its name.
  const chapterHeading = screen.getByRole('heading', { level: 2, name: introduction.title });
  const beside = chapterHeading.parentElement;
  expect(beside).not.toBeNull();
  expect(within(beside ?? document.body).getByText(t('learning.player.state.done'))).toBeInTheDocument();
  expect(screen.queryByText(t('learning.player.text.hint'))).toBeNull();
});

test('the last chapter done shows a lasting panel: the content is completed', async () => {
  progress = {
    [handwashing.chapterId]: { percent: 100, completed: true },
    [summary.chapterId]: { percent: 100, completed: true },
  };
  reporting = async (body) => {
    status = 'ContentCompleted';
    return storing(body);
  };
  await renderPage();

  FakeIntersectionObserver.show();

  await waitFor(() =>
    expect(
      screen
        .getAllByRole('status')
        .filter((region) => region.textContent?.includes(t('learning.player.contentCompleted'))),
    ).not.toHaveLength(0),
  );
});

test('a report that failed shows an alert with Save now, which sends it again and clears it', async () => {
  reporting = async () => new Response('', { status: 503 });
  await renderPage();

  FakeIntersectionObserver.show();
  const alert = await screen.findByRole('alert');
  expect(alert).toHaveTextContent(t('learning.player.progress.failed'));

  reporting = storing;
  await userEvent.click(within(alert).getByRole('button', { name: t('learning.player.progress.saveNow') }));

  await waitFor(() => expect(screen.queryByRole('alert')).toBeNull());
  expect(reports()).toEqual([
    { chapterId: introduction.chapterId, percent: 100 },
    { chapterId: introduction.chapterId, percent: 100 },
  ]);
});

test('a finished session says so and reports no more', async () => {
  reporting = async () => refusal(409, 'learning.session-finished');
  await renderPage();

  FakeIntersectionObserver.show();
  expect(await screen.findByRole('alert')).toHaveTextContent(t('learning.player.progress.finished'));
  FakeIntersectionObserver.show();
  window.dispatchEvent(new Event('pagehide'));

  expect(reports()).toHaveLength(1);
});

test('nothing about progress is kept in the browser: the server is the only record', async () => {
  const setItem = vi.spyOn(Storage.prototype, 'setItem');
  const open = vi.fn();
  vi.stubGlobal('indexedDB', { open });
  onTestFinished(() => {
    setItem.mockRestore();
    vi.unstubAllGlobals();
    vi.stubGlobal('fetch', host);
    FakeIntersectionObserver.install();
  });
  await renderPage();

  FakeIntersectionObserver.show();
  await waitFor(() => expect(reports()).toHaveLength(1));
  await waitFor(() => expect(statusOf(t('learning.player.chapterDone', { title: introduction.title }))).not.toBeNull());

  expect(setItem).not.toHaveBeenCalled();
  expect(open).not.toHaveBeenCalled();
  expect(sessionStorage.length).toBe(0);
});
