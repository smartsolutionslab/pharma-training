import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { render, screen, waitFor, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import { createMemoryHistory, type Router } from 'vue-router';
import type { CatalogChapterDto, LearningMediaUrlDto } from '@/api/generated';
import { setLanguage, t } from '@/i18n';
import { createAppRouter } from '@/router';
import {
  type ChapterProgress,
  courseVersion,
  handwashing,
  introduction,
  session,
  sessionId,
  summary,
  versionId,
} from './coursePlayerFixtures';
import CoursePlayerPage from './CoursePlayerPage.vue';
import { FakeIntersectionObserver } from './fakeIntersectionObserver';
import { FakeMedia } from './fakeMedia';

// Moving between chapters (#44, Q4 decided: free, in any order). Each item of the overview is a link to its chapter
// (?chapter=), the current one marked aria-current="step"; Previous and Next chapter buttons move by one. Whatever way
// the chapter changes, what was pending is reported first, then ?chapter= changes and the focus goes to the new
// chapter's h2.
FakeMedia.install();
FakeIntersectionObserver.install();
const host = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>();
vi.stubGlobal('fetch', host);

let progress: Record<string, ChapterProgress>;
let router: Router;
/** The chapter in the address at the moment each report was sent. */
let reportedOn: unknown[];

beforeEach(() => {
  FakeIntersectionObserver.reset();
  progress = {};
  reportedOn = [];
  host.mockImplementation(async (input, init) => {
    const url = new URL(String(input), 'http://localhost');
    const method = init?.method ?? 'GET';
    if (method === 'GET' && url.pathname === `/api/catalog/course-versions/${versionId}`) {
      return Response.json(courseVersion());
    }
    if (method === 'POST' && url.pathname === '/api/learning/sessions') {
      return Response.json(session(progress), { status: 201 });
    }
    if (method === 'GET' && url.pathname === `/api/learning/sessions/${sessionId}`) {
      return Response.json(session(progress));
    }
    if (method === 'GET' && url.pathname === `/api/learning/media/${handwashing.chapterId}`) {
      const answer: LearningMediaUrlDto = {
        url: 'https://storage.example/media/handwashing.mp4?sig=nav',
        expiresAt: new Date(Date.now() + 15 * 60_000).toISOString(),
        contentType: 'video/mp4',
        kind: 'Video',
      };
      return Response.json(answer);
    }
    if (method === 'POST' && url.pathname === `/api/learning/sessions/${sessionId}/progress`) {
      reportedOn.push(router.currentRoute.value.query.chapter);
      const body = JSON.parse(String(init?.body)) as { chapterId: string; percent: number };
      progress = { ...progress, [body.chapterId]: { percent: body.percent, completed: body.percent >= 95 } };
      return Response.json(session(progress));
    }
    return new Response('', { status: 404 });
  });
});
afterEach(() => host.mockReset());

const reports = () =>
  host.mock.calls
    .filter(([input]) => String(input).endsWith('/progress'))
    .map(([, init]) => JSON.parse(String(init?.body)) as unknown);

/** The page on a chapter; resolves once its h2 shows. */
async function renderPage(chapter: CatalogChapterDto) {
  router = createAppRouter(createMemoryHistory());
  await router.push(`/learning/courses/${versionId}?chapter=${chapter.chapterId}`);
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  const rendered = render(CoursePlayerPage, { global: { plugins: [router, [VueQueryPlugin, { queryClient }]] } });
  await screen.findByRole('heading', { level: 2, name: chapter.title });
  return rendered;
}

const overview = () => screen.getByRole('navigation', { name: t('learning.player.chapters') });
const overviewItem = (chapter: CatalogChapterDto) => {
  const item = within(overview())
    .getAllByRole('listitem')
    .find((listItem) => listItem.textContent?.includes(chapter.title) === true);
  expect(item).toBeDefined();
  return item ?? document.body;
};
const linkTo = (chapter: CatalogChapterDto) =>
  within(overviewItem(chapter)).getByRole('link', { name: new RegExp(chapter.title) });

/** The chapter is the one shown: current in the overview, headed, in ?chapter=, and its h2 has the focus. */
async function expectShown(chapter: CatalogChapterDto) {
  const chapterHeading = await screen.findByRole('heading', { level: 2, name: chapter.title });
  await waitFor(() => expect(router.currentRoute.value.query.chapter).toBe(chapter.chapterId));
  expect(overviewItem(chapter)).toHaveAttribute('aria-current', 'step');
  await waitFor(() => expect(chapterHeading).toHaveFocus());
}

/** A button the learner cannot use here: absent or disabled. */
function expectUnavailable(name: string) {
  const button = screen.queryByRole('button', { name });
  expect(button === null || button.hasAttribute('disabled')).toBe(true);
}

test('each chapter in the overview is a link to it, kept in ?chapter=', async () => {
  await renderPage(introduction);

  for (const chapter of [introduction, handwashing, summary]) {
    const href = linkTo(chapter).getAttribute('href') ?? '';
    const target = new URL(href, 'http://localhost');
    expect(target.pathname).toBe(`/learning/courses/${versionId}`);
    expect(target.searchParams.get('chapter')).toBe(chapter.chapterId);
  }
});

test('any chapter can be opened from the overview, in any order: it becomes current and its heading takes the focus', async () => {
  await renderPage(introduction);

  await userEvent.click(linkTo(summary));
  await expectShown(summary);
  expect(overviewItem(introduction)).not.toHaveAttribute('aria-current');

  await userEvent.click(linkTo(introduction));
  await expectShown(introduction);
});

test('Next chapter moves to the following chapter, and Previous chapter back', async () => {
  await renderPage(introduction);

  await userEvent.click(screen.getByRole('button', { name: 'Nächstes Kapitel' }));
  await expectShown(handwashing);
  await userEvent.click(screen.getByRole('button', { name: 'Nächstes Kapitel' }));
  await expectShown(summary);

  await userEvent.click(screen.getByRole('button', { name: 'Vorheriges Kapitel' }));
  await expectShown(handwashing);
});

test('there is no chapter before the first, nor after the last', async () => {
  await renderPage(introduction);
  expect(screen.getByRole('button', { name: 'Nächstes Kapitel' })).toBeEnabled();
  expectUnavailable('Vorheriges Kapitel');

  await userEvent.click(linkTo(summary));
  await expectShown(summary);
  expect(screen.getByRole('button', { name: 'Vorheriges Kapitel' })).toBeEnabled();
  expectUnavailable('Nächstes Kapitel');
});

test('the buttons are in English once switched', async () => {
  setLanguage('en-GB', { remember: false });
  await renderPage(handwashing);

  expect(screen.getByRole('button', { name: 'Next chapter' })).toBeEnabled();
  expect(screen.getByRole('button', { name: 'Previous chapter' })).toBeEnabled();
});

/** The page on the video, played for 12 s: 2 % tracked and not sent yet (the 30 s have not passed). */
async function pendingOnTheVideo() {
  const { container } = await renderPage(handwashing);
  const video = await waitFor(() => {
    const element = container.querySelector('video');
    expect(element).not.toBeNull();
    return element ?? document.createElement('video');
  });
  const media = FakeMedia.of(video);
  media.loadMetadata(600);
  media.play();
  media.playFor(12);
  expect(reports()).toEqual([]);
}

test('Next chapter first reports what was pending, then moves on', async () => {
  await pendingOnTheVideo();

  await userEvent.click(screen.getByRole('button', { name: 'Nächstes Kapitel' }));

  await expectShown(summary);
  await waitFor(() => expect(reports()).toEqual([{ chapterId: handwashing.chapterId, percent: 2 }]));
  expect(reportedOn).toEqual([handwashing.chapterId]);
});

test('a chapter opened from the overview first reports what was pending too', async () => {
  await pendingOnTheVideo();

  await userEvent.click(linkTo(introduction));

  await expectShown(introduction);
  await waitFor(() => expect(reports()).toEqual([{ chapterId: handwashing.chapterId, percent: 2 }]));
});
