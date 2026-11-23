import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { cleanup, render, screen, waitFor, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import { createMemoryHistory } from 'vue-router';
import type { CatalogChapterDto, CatalogCourseVersionDto, LearningSessionDto } from '@/api/generated';
import { i18n, setLanguage, t } from '@/i18n';
import { createAppRouter } from '@/router';
import {
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

// The chapter player (#44) at /learning/courses/:courseVersionId: it reads the version from the catalog, starts or
// resumes the learner's session (POST /learning/sessions, idempotent), and shows the chapters with the current one.
const host = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>();
vi.stubGlobal('fetch', host);
FakeIntersectionObserver.install();

let catalogReading: () => Promise<Response>;
let starting: () => Promise<Response>;
let sessionReading: () => Promise<Response>;
let started: LearningSessionDto;

function hostAnswering(version: CatalogCourseVersionDto, answer: LearningSessionDto) {
  started = answer;
  catalogReading = async () => Response.json(version);
  starting = async () => Response.json(started, { status: 201 });
  sessionReading = async () => Response.json(started);
}

beforeEach(() => {
  FakeIntersectionObserver.reset();
  hostAnswering(courseVersion(), session());
  host.mockImplementation(async (input, init) => {
    const url = new URL(String(input), 'http://localhost');
    const method = init?.method ?? 'GET';
    if (method === 'GET' && url.pathname === `/api/catalog/course-versions/${versionId}`) {
      return catalogReading();
    }
    if (method === 'POST' && url.pathname === '/api/learning/sessions') {
      return starting();
    }
    if (method === 'GET' && url.pathname === `/api/learning/sessions/${sessionId}`) {
      return sessionReading();
    }
    return new Response('', { status: 404 });
  });
});
afterEach(() => host.mockReset());
// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(cleanup);

/** What the page asked of the host, by method and path below /api, with the body it sent. */
const requests = () =>
  host.mock.calls.map(([input, init]) => ({
    method: init?.method ?? 'GET',
    path: new URL(String(input), 'http://localhost').pathname.replace(/^\/api/, ''),
    body: init?.body === undefined ? undefined : (JSON.parse(String(init.body)) as unknown),
  }));
const starts = () => requests().filter(({ method, path }) => method === 'POST' && path === '/learning/sessions');
const catalogReads = () => requests().filter(({ path }) => path.startsWith('/catalog/'));

/** The page at its address, with a query client of its own that does not retry, so a failure shows at once. */
async function renderPage(address = `/learning/courses/${versionId}`) {
  const router = createAppRouter(createMemoryHistory());
  await router.push(address);
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  const { container } = render(CoursePlayerPage, { global: { plugins: [router, [VueQueryPlugin, { queryClient }]] } });
  return { router, queryClient, container };
}

const title = courseVersion().title;
const heading = () => screen.findByRole('heading', { level: 1, name: title });
const overview = () => screen.getByRole('navigation', { name: t('learning.player.chapters') });
const overviewItem = (chapter: CatalogChapterDto) =>
  within(overview())
    .getAllByRole('listitem')
    .find((item) => item.textContent?.includes(chapter.title) === true);
const percent = (value: number) => i18n.global.n(value, 'percent');

test('the page starts or resumes the session on the version, once', async () => {
  await renderPage();
  await heading();

  expect(starts()).toEqual([{ method: 'POST', path: '/learning/sessions', body: { courseVersionId: versionId } }]);
});

test("the page is headed by the course's title and lists its chapters in order, each with its kind and state", async () => {
  hostAnswering(
    courseVersion(),
    session({
      [introduction.chapterId]: { percent: 100, completed: true },
      [handwashing.chapterId]: { percent: 40, completed: false },
    }),
  );
  await renderPage();
  await heading();

  const list = within(overview()).getByRole('list');
  expect(list.tagName).toBe('OL');
  const items = within(list).getAllByRole('listitem');
  expect(items).toHaveLength(3);
  expect(items[0]).toHaveTextContent(introduction.title);
  expect(items[1]).toHaveTextContent(handwashing.title);
  expect(items[2]).toHaveTextContent(summary.title);

  expect(overviewItem(introduction)).toHaveTextContent(t('glossary.chapterKind.Text'));
  expect(overviewItem(introduction)).toHaveTextContent(t('learning.player.state.done'));
  expect(overviewItem(handwashing)).toHaveTextContent(t('glossary.chapterKind.Video'));
  expect(overviewItem(handwashing)).toHaveTextContent(t('learning.player.state.progress', { percent: percent(0.4) }));
  expect(overviewItem(summary)).toHaveTextContent(t('learning.player.state.open'));
});

test('the overview names the states in English once switched', async () => {
  setLanguage('en-GB', { remember: false });
  hostAnswering(
    courseVersion(),
    session({
      [introduction.chapterId]: { percent: 100, completed: true },
      [handwashing.chapterId]: { percent: 40, completed: false },
    }),
  );
  await renderPage();
  await heading();

  expect(screen.getByRole('navigation', { name: 'Chapters' })).toBeInTheDocument();
  expect(overviewItem(introduction)).toHaveTextContent('Done');
  expect(overviewItem(handwashing)).toHaveTextContent(`${percent(0.4)} done`);
  expect(overviewItem(summary)).toHaveTextContent('Not started');
});

test('the current chapter is the first one not done, marked as the current step, headed, and kept in ?chapter=', async () => {
  hostAnswering(
    courseVersion(),
    session({
      [introduction.chapterId]: { percent: 100, completed: true },
      [handwashing.chapterId]: { percent: 100, completed: true },
    }),
  );
  const { router } = await renderPage();
  await heading();

  await waitFor(() => expect(overviewItem(summary)).toHaveAttribute('aria-current', 'step'));
  expect(overviewItem(introduction)).not.toHaveAttribute('aria-current');
  expect(screen.getByRole('heading', { level: 2, name: summary.title })).toBeInTheDocument();
  await waitFor(() => expect(router.currentRoute.value.query.chapter).toBe(summary.chapterId));
});

test('with every chapter done the current chapter is the first', async () => {
  hostAnswering(
    courseVersion(),
    session(
      {
        [introduction.chapterId]: { percent: 100, completed: true },
        [handwashing.chapterId]: { percent: 100, completed: true },
        [summary.chapterId]: { percent: 100, completed: true },
      },
      'ContentCompleted',
    ),
  );
  await renderPage();
  await heading();

  await waitFor(() => expect(overviewItem(introduction)).toHaveAttribute('aria-current', 'step'));
  expect(screen.getByRole('heading', { level: 2, name: introduction.title })).toBeInTheDocument();
});

test('?chapter= picks the chapter shown', async () => {
  await renderPage(`/learning/courses/${versionId}?chapter=${summary.chapterId}`);
  await heading();

  await waitFor(() => expect(overviewItem(summary)).toHaveAttribute('aria-current', 'step'));
  expect(screen.getByRole('heading', { level: 2, name: summary.title })).toBeInTheDocument();
});

test.each([
  ['a chapter not in the course', '0198c1a0-0000-7000-8000-0000000000ff'],
  ['a chapter that is not an id', 'not-a-chapter'],
])('?chapter= naming %s falls back to the first chapter not done', async (_case, chapter) => {
  const { router } = await renderPage(`/learning/courses/${versionId}?chapter=${chapter}`);
  await heading();

  await waitFor(() => expect(overviewItem(introduction)).toHaveAttribute('aria-current', 'step'));
  expect(screen.getByRole('heading', { level: 2, name: introduction.title })).toBeInTheDocument();
  await waitFor(() => expect(router.currentRoute.value.query.chapter).toBe(introduction.chapterId));
});

test('a version id that is not an id is never sent, and the page says the course does not exist', async () => {
  await renderPage('/learning/courses/not-a-version');

  expect(await screen.findByRole('alert')).toHaveTextContent(t('learning.player.notFound'));
  expect(requests().filter(({ path }) => path.startsWith('/catalog/') || path.startsWith('/learning/'))).toEqual([]);
});

test('while the course loads the page is busy and shows skeletons shaped like its heading, three chapters and the content', async () => {
  catalogReading = () => new Promise<Response>(() => undefined);
  starting = () => new Promise<Response>(() => undefined);
  const { container } = await renderPage();

  await waitFor(() => expect(container.querySelectorAll('[data-slot="skeleton"]').length).toBeGreaterThanOrEqual(5));
  expect(container.querySelector('[aria-busy="true"]')).not.toBeNull();
  expect(screen.queryByRole('heading', { level: 1 })).toBeNull();
});

test.each([
  [
    'the catalog has no such version',
    'notFound',
    () => {
      catalogReading = async () => refusal(404, 'catalog.course-version-not-found');
    },
  ],
  [
    'the version is not in Learning yet',
    'notReady',
    () => {
      starting = async () => refusal(404, 'learning.course-version-not-found');
    },
  ],
  [
    "the learner's identity is incomplete",
    'noIdentity',
    () => {
      starting = async () => refusal(403, 'learning.identity-incomplete');
    },
  ],
  [
    'the start fails',
    'loadFailed',
    () => {
      starting = async () => new Response('', { status: 500 });
    },
  ],
  [
    'the catalog fails',
    'loadFailed',
    () => {
      catalogReading = async () => new Response('', { status: 503 });
    },
  ],
])('when %s the page says so in an alert, and Retry loads it again', async (_case, message, refuse) => {
  const answering = { catalogReading, starting };
  refuse();
  await renderPage();

  const alert = await screen.findByRole('alert');
  expect(alert).toHaveTextContent(t(`learning.player.${message}`));
  const before = { starts: starts().length, catalogReads: catalogReads().length };

  catalogReading = answering.catalogReading;
  starting = answering.starting;
  await userEvent.click(within(alert).getByRole('button', { name: t('shell.retry') }));

  const h1 = await heading();
  await waitFor(() => expect(h1).toHaveFocus());
  expect(screen.queryByRole('alert')).toBeNull();
  // What failed is asked again: the catalog read again, or the start posted again.
  expect(starts().length + catalogReads().length).toBeGreaterThan(before.starts + before.catalogReads);
});

test('a refused start posts again on Retry', async () => {
  starting = async () => refusal(404, 'learning.course-version-not-found');
  await renderPage();
  const alert = await screen.findByRole('alert');

  starting = async () => Response.json(started, { status: 201 });
  await userEvent.click(within(alert).getByRole('button', { name: t('shell.retry') }));

  await heading();
  expect(starts()).toHaveLength(2);
});

test('the page picks its words by the code, never by what the host wrote', async () => {
  starting = async () => refusal(403, 'learning.identity-incomplete');
  await renderPage();

  const alert = await screen.findByRole('alert');
  expect(alert).toHaveTextContent(t('learning.player.noIdentity'));
  expect(alert).not.toHaveTextContent('Not for the user.');
});

test('a start that lost a race (already-exists) is posted again once, without a message', async () => {
  let answers = 0;
  starting = async () => {
    answers += 1;
    return answers === 1 ? refusal(409, 'already-exists') : Response.json(started, { status: 200 });
  };
  await renderPage();

  await heading();
  expect(starts()).toHaveLength(2);
  expect(screen.queryByRole('alert')).toBeNull();
});

test('a course without chapters shows the empty state', async () => {
  hostAnswering(courseVersion([]), session({}, 'InProgress', []));
  const { container } = await renderPage();
  await heading();

  const empty = await screen.findByText(t('learning.player.empty'));
  expect(empty.closest('[data-slot="empty-state"]')).not.toBeNull();
  expect(container.querySelector('[aria-current="step"]')).toBeNull();
});

test('the empty state speaks English once switched', async () => {
  setLanguage('en-GB', { remember: false });
  hostAnswering(courseVersion([]), session({}, 'InProgress', []));
  await renderPage();

  expect(await screen.findByText('This course has no chapters.')).toBeInTheDocument();
});
