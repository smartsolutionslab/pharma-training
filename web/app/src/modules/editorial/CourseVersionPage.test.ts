import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { cleanup, render, screen, waitFor, within } from '@testing-library/vue';
import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest';
import { axe } from 'vitest-axe';
import { createMemoryHistory } from 'vue-router';
import type {
  EditorialChapterDto,
  EditorialCourseVersionDto,
  EditorialCourseVersionStatus,
  EditorialQuestionDto,
} from '@/api/generated';
import { setLanguage, t } from '@/i18n';
import { createAppRouter } from '@/router';
import CourseVersionPage from './CourseVersionPage.vue';
import { route } from './routes';

const notify = vi.hoisted(() => ({ success: vi.fn(), failure: vi.fn() }));
vi.mock('@/lib/notify', () => ({ notify }));

// The host, as the generated client reaches it: GET /editorial/course-versions/{id} answers the version the test gives
// it, and the chapter requests answer what each test says (204 by default).
const host = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>();
vi.stubGlobal('fetch', host);

const versionId = '0198c1a0-0000-7000-8000-0000000000c1';

const introduction: EditorialChapterDto = {
  id: '0198c1a0-0000-7000-8000-0000000000e1',
  position: 1,
  title: 'Einleitung',
  kind: 'Text',
  text: '# Willkommen\n\nDas ist **wichtig**.',
  file: null,
  durationSeconds: null,
};
const handout: EditorialChapterDto = {
  id: '0198c1a0-0000-7000-8000-0000000000e2',
  position: 2,
  title: 'Handout',
  kind: 'Pdf',
  text: null,
  file: `course-versions/${versionId}/0198c1a0-0000-7000-8000-0000000000f2.pdf`,
  durationSeconds: null,
};
const handwashing: EditorialChapterDto = {
  id: '0198c1a0-0000-7000-8000-0000000000e3',
  position: 3,
  title: 'Händewaschen im Video',
  kind: 'Video',
  text: null,
  file: `course-versions/${versionId}/0198c1a0-0000-7000-8000-0000000000f3.mp4`,
  durationSeconds: 725,
};

/** A question about the chapter, approved or not yet reviewed. */
function question(id: string, chapterId: string, review: 'Approved' | 'Unreviewed'): EditorialQuestionDto {
  return {
    id,
    position: 1,
    chapterId,
    text: 'Wie lange waschen Sie die Hände?',
    type: 'SingleChoice',
    options: [
      { id: `${id}-1`, text: '30 Sekunden', correct: true },
      { id: `${id}-2`, text: '5 Sekunden', correct: false },
    ],
    provenance: 'Human',
    review,
    reviewedBy: null,
    reviewedAt: null,
  };
}

/** The version as the host answers it, a draft with a text, a PDF and a video chapter unless the test says otherwise. */
function versionOf(
  status: EditorialCourseVersionStatus,
  chapters: EditorialChapterDto[] = [introduction, handout, handwashing],
): EditorialCourseVersionDto {
  return {
    id: versionId,
    courseId: '0198c1a0-0000-7000-8000-0000000000d1',
    number: 2,
    title: 'Hygiene in der Apotheke',
    status,
    chapters,
    questions: [
      question('0198c1a0-0000-7000-8000-0000000000a1', introduction.id, 'Approved'),
      question('0198c1a0-0000-7000-8000-0000000000a2', introduction.id, 'Unreviewed'),
      question('0198c1a0-0000-7000-8000-0000000000a3', handwashing.id, 'Approved'),
    ],
    blueprint: { questions: 10, passingScorePercent: 80, validityMonths: 12 },
  };
}

let version: EditorialCourseVersionDto;
let reading: () => Promise<Response>;
/** What the host answers a chapter request (POST, PUT or DELETE below the version), by default 204. */
let changing: (method: string, path: string, body: unknown) => Promise<Response>;

const read = async () => Response.json(version);

beforeEach(() => {
  version = versionOf('Draft');
  reading = read;
  changing = async () => new Response(null, { status: 204 });
  host.mockImplementation(async (input, init) => {
    const url = new URL(String(input), 'http://localhost');
    const method = init?.method ?? 'GET';
    if (method === 'GET' && url.pathname === `/api/editorial/course-versions/${versionId}`) {
      return reading();
    }
    if (url.pathname.startsWith(`/api/editorial/course-versions/${versionId}/`)) {
      return changing(method, url.pathname, init?.body === undefined ? undefined : JSON.parse(String(init.body)));
    }
    return new Response('', { status: 404 });
  });
  notify.success.mockReset();
  notify.failure.mockReset();
});
afterEach(() => host.mockReset());
// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(cleanup);

/** What the page asked of the host, by method and path below /api. */
const requests = () =>
  host.mock.calls.map(([input, init]) => ({
    method: init?.method ?? 'GET',
    path: new URL(String(input), 'http://localhost').pathname.replace(/^\/api/, ''),
  }));
const reads = () => requests().filter(({ method }) => method === 'GET');

/** The page at its address, with a query client of its own that does not retry, so a failure shows at once. */
async function renderPage(id = versionId) {
  const router = createAppRouter(createMemoryHistory());
  await router.push(`/editorial/versions/${id}`);
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  const { container } = render(CourseVersionPage, { global: { plugins: [router, [VueQueryPlugin, { queryClient }]] } });
  await screen.findByRole('heading', { level: 1, name: version.title });
  return { router, queryClient, container };
}

const chapterList = () => screen.getByRole('list', { name: t('editorial.chapters.title') });
const chapterItem = (chapter: EditorialChapterDto) =>
  within(chapterList())
    .getAllByRole('listitem')
    .find((item) => within(item).queryByText(chapter.title) !== null) ?? null;

/** The buttons an editor changes a chapter with, by their names for the chapter. */
const chapterActions = (chapter: EditorialChapterDto) => [
  t('editorial.chapters.edit', { title: chapter.title }),
  t('editorial.chapters.remove', { title: chapter.title }),
  t('editorial.chapters.moveUp', { title: chapter.title }),
  t('editorial.chapters.moveDown', { title: chapter.title }),
];

describe('a draft', () => {
  test('is headed by its title, with its number, its status and the way back to its inbox tab', async () => {
    await renderPage();

    expect(screen.getByRole('heading', { level: 1, name: 'Hygiene in der Apotheke' })).toBeInTheDocument();
    expect(screen.getByText(t('editorial.version.number', { number: 2 }))).toBeInTheDocument();
    expect(screen.getByText(t('glossary.courseVersionStatus.Draft'))).toBeInTheDocument();
    expect(screen.getByRole('link', { name: t('editorial.version.backToInbox') })).toHaveAttribute(
      'href',
      '/editorial?status=Draft',
    );
  });

  test('lists its chapters in order, each with its position, title and kind, and a video its duration', async () => {
    await renderPage();

    expect(screen.getByRole('heading', { level: 2, name: t('editorial.chapters.title') })).toBeInTheDocument();
    expect(chapterList().tagName).toBe('OL');
    const items = within(chapterList()).getAllByRole('listitem');
    expect(items).toHaveLength(3);
    expect(items[0]).toHaveTextContent(t('editorial.chapters.position', { position: 1 }));
    expect(items[0]).toHaveTextContent(introduction.title);
    expect(items[0]).toHaveTextContent(t('glossary.chapterKind.Text'));
    expect(items[1]).toHaveTextContent(t('editorial.chapters.position', { position: 2 }));
    expect(items[1]).toHaveTextContent(handout.title);
    expect(items[1]).toHaveTextContent(t('glossary.chapterKind.Pdf'));
    expect(items[2]).toHaveTextContent(t('editorial.chapters.position', { position: 3 }));
    expect(items[2]).toHaveTextContent(handwashing.title);
    expect(items[2]).toHaveTextContent(t('glossary.chapterKind.Video'));
    expect(items[2]).toHaveTextContent(t('editorial.chapters.duration', { duration: '12:05' }));
  });

  test('offers to add a chapter, and per chapter to edit, delete and move it, each button naming its chapter', async () => {
    await renderPage();

    expect(screen.getByRole('button', { name: t('editorial.chapters.add') })).toBeEnabled();
    for (const chapter of [introduction, handout, handwashing]) {
      const item = within(chapterItem(chapter) ?? document.body);
      for (const name of chapterActions(chapter)) {
        expect(item.getByRole('button', { name })).toBeInTheDocument();
      }
      // The handle drags the chapter with a mouse; the buttons move it by keyboard, touch and screen reader.
      expect(item.getByTitle(t('editorial.chapters.dragHint'))).toHaveAttribute('aria-hidden', 'true');
    }
    // The first cannot move up, the last cannot move down.
    expect(
      screen.getByRole('button', { name: t('editorial.chapters.moveUp', { title: introduction.title }) }),
    ).toBeDisabled();
    expect(
      screen.getByRole('button', { name: t('editorial.chapters.moveDown', { title: introduction.title }) }),
    ).toBeEnabled();
    expect(
      screen.getByRole('button', { name: t('editorial.chapters.moveDown', { title: handwashing.title }) }),
    ).toBeDisabled();
    expect(
      screen.getByRole('button', { name: t('editorial.chapters.moveUp', { title: handwashing.title }) }),
    ).toBeEnabled();
    expect(screen.queryByText(/editorial\.version\.readOnly/)).not.toBeInTheDocument();
  });

  test('without chapters says so, with the action that adds the first', async () => {
    version = versionOf('Draft', []);
    await renderPage();

    const sentence = await screen.findByText(t('editorial.chapters.empty'));
    const emptyState = within(sentence.closest('[data-slot="empty-state"]') as HTMLElement);
    expect(emptyState.getByRole('button', { name: t('editorial.chapters.add') })).toBeEnabled();
  });

  // The German texts themselves, not only their keys: a key that resolved to the wrong text would pass the t() queries.
  test('speaks German by default', async () => {
    await renderPage();

    expect(screen.getByText('Version 2')).toBeInTheDocument();
    expect(screen.getByText('Entwurf')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Zurück zum Eingang' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 2, name: 'Kapitel' })).toBeInTheDocument();
    expect(chapterItem(handwashing)).toHaveTextContent('Kapitel 3');
    expect(chapterItem(handwashing)).toHaveTextContent('Dauer 12:05');
    expect(screen.getByRole('button', { name: 'Kapitel hinzufügen' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /^„Handout. löschen$/ })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /^„Handout. nach oben verschieben$/ })).toBeInTheDocument();
    expect(within(chapterItem(handout) ?? document.body).getByTitle('Zum Verschieben ziehen')).toBeInTheDocument();
  });

  test('without chapters says so in German', async () => {
    version = versionOf('Draft', []);
    await renderPage();

    expect(await screen.findByText('Diese Version hat noch keine Kapitel.')).toBeVisible();
  });

  test('switched to English, speaks English', async () => {
    await renderPage();

    setLanguage('en-GB', { remember: false });

    expect(await screen.findByText('Version 2')).toBeInTheDocument();
    expect(screen.getByText('Draft')).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 2, name: 'Chapters' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Add a chapter' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Edit “Handout”' })).toBeInTheDocument();
    expect(chapterItem(handwashing)).toHaveTextContent('Duration 12:05');
  });
});

// Read-only unless the version is a draft (spec 2.3): no form and no add, edit, delete, move, drag or upload control in
// the DOM, not only disabled ones, and nothing asked of the host but the version.
describe.each([
  ['InReview' as const, '/editorial?status=InReview'],
  ['Published' as const, '/editorial?status=Published'],
  ['Superseded' as const, /^\/editorial(\?status=Draft)?$/],
  ['Retired' as const, /^\/editorial(\?status=Draft)?$/],
])('a version %s', (status, inbox) => {
  beforeEach(() => {
    version = versionOf(status);
  });

  test('says why it cannot be edited, and shows its status and the way back to the inbox', async () => {
    await renderPage();

    expect(screen.getByText(t(`editorial.version.readOnly.${status}`))).toBeInTheDocument();
    expect(screen.getByText(t(`glossary.courseVersionStatus.${status}`))).toBeInTheDocument();
    const back = screen.getByRole('link', { name: t('editorial.version.backToInbox') });
    if (typeof inbox === 'string') {
      expect(back).toHaveAttribute('href', inbox);
    } else {
      expect(back.getAttribute('href')).toMatch(inbox);
    }
  });

  test('has no control that would change it', async () => {
    const { container } = await renderPage();

    const names = screen
      .queryAllByRole('button')
      .map((button) => [button.textContent, button.getAttribute('aria-label'), button.title].join(' '));
    for (const chapter of [introduction, handout, handwashing]) {
      for (const name of chapterActions(chapter)) {
        expect(screen.queryByRole('button', { name })).not.toBeInTheDocument();
      }
    }
    expect(screen.queryByRole('button', { name: t('editorial.chapters.add') })).not.toBeInTheDocument();
    expect(names.filter((name) => /bearbeiten|löschen|verschieben|hinzufügen|speichern|hochladen/i.test(name))).toEqual(
      [],
    );
    expect(screen.queryByTitle(t('editorial.chapters.dragHint'))).not.toBeInTheDocument();
    expect(container.querySelector('form, input, textarea, select, [draggable="true"]')).toBeNull();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  test('asks the host for nothing but the version', async () => {
    await renderPage();

    await waitFor(() => expect(reads()).toEqual([{ method: 'GET', path: `/editorial/course-versions/${versionId}` }]));
    expect(requests().filter(({ method }) => method !== 'GET')).toEqual([]);
  });

  test('shows its chapters read-only: position, title, kind, a video its duration, a file that it was uploaded', async () => {
    await renderPage();

    const items = within(chapterList()).getAllByRole('listitem');
    expect(items).toHaveLength(3);
    expect(items[0]).toHaveTextContent(t('editorial.chapters.position', { position: 1 }));
    expect(items[0]).toHaveTextContent(introduction.title);
    expect(items[0]).toHaveTextContent(t('glossary.chapterKind.Text'));
    expect(items[1]).toHaveTextContent(handout.title);
    expect(items[1]).toHaveTextContent(t('glossary.chapterKind.Pdf'));
    expect(items[1]).toHaveTextContent(t('editorial.chapters.file'));
    expect(items[2]).toHaveTextContent(t('glossary.chapterKind.Video'));
    expect(items[2]).toHaveTextContent(t('editorial.chapters.duration', { duration: '12:05' }));
    expect(items[2]).toHaveTextContent(t('editorial.chapters.file'));
  });

  test("shows a text chapter's Markdown rendered and sanitised, inside details named by the chapter", async () => {
    version = versionOf(status, [
      { ...introduction, text: '# Willkommen\n\nDas ist **wichtig**.\n\n<script>window.hacked = true</script>' },
      handout,
      handwashing,
    ]);
    const { container } = await renderPage();

    const details = [...container.querySelectorAll('details')].find((each) =>
      each.querySelector('summary')?.textContent.includes(introduction.title),
    );
    expect(details).toBeDefined();
    // Below the page's h1 and the chapters' h2: the content's top heading is an h3.
    expect(details?.querySelector('h1')).toBeNull();
    expect(details?.querySelector('h3')?.textContent).toBe('Willkommen');
    expect(details?.querySelector('strong')?.textContent).toBe('wichtig');
    expect(details?.querySelector('script')).toBeNull();
    expect(details?.textContent).toContain('<script>window.hacked = true</script>');
  });
});

test('a published version says so in German', async () => {
  version = versionOf('Published');
  await renderPage();

  expect(
    screen.getByText(
      'Diese Version ist veröffentlicht und kann nicht mehr geändert werden. Änderungen machen Sie in einer neuen Version.',
    ),
  ).toBeInTheDocument();
  expect(screen.getByText('Veröffentlicht')).toBeInTheDocument();
  expect(chapterItem(handout)).toHaveTextContent('Datei hochgeladen');
});

test('a version in review says so in German', async () => {
  version = versionOf('InReview');
  await renderPage();

  expect(screen.getByText('Diese Version ist in Prüfung und kann nicht bearbeitet werden.')).toBeInTheDocument();
});

test.each([['de-DE' as const], ['en-GB' as const]])(
  'a published version has no accessibility violations axe can find, in %s',
  async (language) => {
    setLanguage(language, { remember: false });
    version = versionOf('Published');
    const { container } = await renderPage();

    expect(await axe(container)).toHaveNoViolations();
  },
);

test('while the version loads the page shows skeletons shaped like its header and chapters', async () => {
  reading = () => new Promise<Response>(() => undefined);
  const router = createAppRouter(createMemoryHistory());
  await router.push(`/editorial/versions/${versionId}`);
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  const { container } = render(CourseVersionPage, { global: { plugins: [router, [VueQueryPlugin, { queryClient }]] } });

  await waitFor(() => expect(container.querySelectorAll('[data-slot="skeleton"]').length).toBeGreaterThanOrEqual(4));
  expect(screen.queryByRole('heading', { level: 1, name: version.title })).not.toBeInTheDocument();
  expect(screen.queryByRole('alert')).not.toBeInTheDocument();
});

/** The page at its address while the host answers the version with the response. */
async function renderAnswering(response: () => Response) {
  reading = async () => response();
  const router = createAppRouter(createMemoryHistory());
  await router.push(`/editorial/versions/${versionId}`);
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(CourseVersionPage, { global: { plugins: [router, [VueQueryPlugin, { queryClient }]] } });
}

test('a version the host does not know says it does not exist', async () => {
  await renderAnswering(
    () =>
      new Response(
        JSON.stringify({
          title: 'Not Found',
          status: 404,
          detail: 'Die Version gibt es nicht.',
          code: 'editorial.course-version-not-found',
        }),
        { status: 404, headers: { 'Content-Type': 'application/problem+json' } },
      ),
  );

  expect(await screen.findByText(t('editorial.version.notFound'))).toBeVisible();
  expect(screen.getByText('Diese Version gibt es nicht.')).toBeVisible();
  expect(screen.queryByRole('heading', { level: 2, name: t('editorial.chapters.title') })).not.toBeInTheDocument();
});

test('a version the host cannot give is reported', async () => {
  await renderAnswering(() => new Response('', { status: 500 }));

  expect(await screen.findByRole('alert')).toHaveTextContent(t('editorial.version.loadFailed'));
  expect(screen.getByRole('alert')).toHaveTextContent('Die Version konnte nicht geladen werden.');
  expect(screen.queryByText(t('editorial.version.notFound'))).not.toBeInTheDocument();
});

// The detail /editorial/versions/:id (T1) opens this page; the placeholder it opened until now is gone.
test('the editorial detail versions/:id opens the course version page', () => {
  const detail = route.details?.find((each) => each.path === 'versions/:id');

  expect(detail?.component).toBe(CourseVersionPage);
  expect(Object.keys(import.meta.glob('./CourseVersionPlaceholder.vue'))).toEqual([]);
});
