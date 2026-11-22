import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
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

// Text chapters (T7): "Kapitel hinzufügen" and a chapter's edit button open the chapter form in a dialog; a chapter's
// delete button asks first. A module's refusal is shown in the host's words (ApiError.serverMessage), else the
// catalog's; either way the version is read again, so the page shows what is now so.

const notADraft = () =>
  new Response(
    JSON.stringify({
      title: 'Conflict',
      status: 409,
      detail: 'Die Version ist kein Entwurf mehr und kann nicht geändert werden.',
      code: 'editorial.not-a-draft',
    }),
    { status: 409, headers: { 'Content-Type': 'application/problem+json' } },
  );

/** What the page sent to change the version, as the host received it. */
const changes = () =>
  host.mock.calls
    .filter(([, init]) => (init?.method ?? 'GET') !== 'GET')
    .map(([input, init]) => ({
      method: init?.method,
      path: new URL(String(input), 'http://localhost').pathname.replace(/^\/api/, ''),
      body: init?.body === undefined ? undefined : (JSON.parse(String(init.body)) as unknown),
    }));

/** The dialog that asks before a chapter is deleted, an alert dialog or a dialog. */
const confirmation = () =>
  screen.queryByRole('alertdialog', { name: t('editorial.chapters.removeConfirm.title') }) ??
  screen.queryByRole('dialog', { name: t('editorial.chapters.removeConfirm.title') });

async function openConfirmation(user: ReturnType<typeof userEvent.setup>, chapter: EditorialChapterDto) {
  await user.click(screen.getByRole('button', { name: t('editorial.chapters.remove', { title: chapter.title }) }));
  await waitFor(() => expect(confirmation()).not.toBeNull());
  return within(confirmation() as HTMLElement);
}

/** Fills the chapter form's fields it is given, each replacing what it held, and saves. */
async function fillAndSave(
  user: ReturnType<typeof userEvent.setup>,
  dialog: ReturnType<typeof within>,
  { title, text }: { title?: string; text?: string },
) {
  if (title !== undefined) {
    const field = dialog.getByRole('textbox', { name: t('editorial.chapterForm.title') });
    await user.clear(field);
    await user.click(field);
    await user.paste(title);
  }
  if (text !== undefined) {
    const field = dialog.getByRole('textbox', { name: t('editorial.chapterForm.text') });
    await user.clear(field);
    await user.click(field);
    await user.paste(text);
  }
  await user.click(dialog.getByRole('button', { name: t('editorial.chapterForm.submit') }));
}

describe('adding a text chapter', () => {
  test('opens the form in a dialog, posts the chapter, reports it, closes and reads the version again', async () => {
    const user = userEvent.setup();
    changing = async () => Response.json('0198c1a0-0000-7000-8000-0000000000e4', { status: 201 });
    await renderPage();

    await user.click(screen.getByRole('button', { name: t('editorial.chapters.add') }));
    const dialog = within(await screen.findByRole('dialog', { name: t('editorial.chapterForm.addTitle') }));
    await fillAndSave(user, dialog, { title: 'Lagerung', text: 'Kühl und **trocken**.' });

    await waitFor(() =>
      expect(notify.success).toHaveBeenCalledWith(t('editorial.chapters.saved', { title: 'Lagerung' })),
    );
    expect(notify.success.mock.calls[0]?.[0]).toMatch(/^Das Kapitel „Lagerung. wurde gespeichert\.$/);
    expect(changes()).toEqual([
      {
        method: 'POST',
        path: `/editorial/course-versions/${versionId}/chapters`,
        body: { title: 'Lagerung', kind: 'Text', text: 'Kühl und **trocken**.', file: null, durationSeconds: null },
      },
    ]);
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    await waitFor(() => expect(reads().length).toBeGreaterThan(1));
    expect(notify.failure).not.toHaveBeenCalled();
  });

  test('from the empty state opens the same form', async () => {
    const user = userEvent.setup();
    version = versionOf('Draft', []);
    await renderPage();

    const sentence = await screen.findByText(t('editorial.chapters.empty'));
    const emptyState = within(sentence.closest('[data-slot="empty-state"]') as HTMLElement);
    await user.click(emptyState.getByRole('button', { name: t('editorial.chapters.add') }));

    expect(await screen.findByRole('dialog', { name: 'Neues Kapitel' })).toBeInTheDocument();
  });

  test('a form left with Abbrechen sends nothing', async () => {
    const user = userEvent.setup();
    await renderPage();

    await user.click(screen.getByRole('button', { name: t('editorial.chapters.add') }));
    const dialog = within(await screen.findByRole('dialog', { name: t('editorial.chapterForm.addTitle') }));
    await user.click(dialog.getByRole('button', { name: t('editorial.cancel') }));

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(changes()).toEqual([]);
  });
});

describe('editing a text chapter', () => {
  test('opens the form filled with the chapter, puts the chapter in its place and reports it', async () => {
    const user = userEvent.setup();
    await renderPage();

    await user.click(screen.getByRole('button', { name: t('editorial.chapters.edit', { title: introduction.title }) }));
    const dialog = within(await screen.findByRole('dialog', { name: t('editorial.chapterForm.editTitle') }));
    expect(dialog.getByRole('textbox', { name: t('editorial.chapterForm.title') })).toHaveValue(introduction.title);
    expect(dialog.getByRole('textbox', { name: t('editorial.chapterForm.text') })).toHaveValue(introduction.text);
    // Its approved question is reviewed again once the chapter is replaced.
    expect(dialog.getByText(t('editorial.chapterForm.requalify', { count: 1 }, 1))).toBeInTheDocument();
    await fillAndSave(user, dialog, { title: 'Einführung' });

    await waitFor(() =>
      expect(notify.success).toHaveBeenCalledWith(t('editorial.chapters.saved', { title: 'Einführung' })),
    );
    expect(changes()).toEqual([
      {
        method: 'PUT',
        path: `/editorial/course-versions/${versionId}/chapters/${introduction.id}`,
        body: { title: 'Einführung', kind: 'Text', text: introduction.text, file: null, durationSeconds: null },
      },
    ]);
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    await waitFor(() => expect(reads().length).toBeGreaterThan(1));
  });

  test('the dialog is named in German', async () => {
    const user = userEvent.setup();
    await renderPage();

    await user.click(screen.getByRole('button', { name: /^„Einleitung. bearbeiten$/ }));

    expect(await screen.findByRole('dialog', { name: 'Kapitel bearbeiten' })).toBeInTheDocument();
  });
});

test.each([
  ['a 409 of the module', notADraft, () => 'Die Version ist kein Entwurf mehr und kann nicht geändert werden.'],
  ['a 500', () => new Response('', { status: 500 }), () => t('editorial.chapters.saveFailed', { title: 'Lagerung' })],
])(
  'a chapter the host refuses with %s is reported, the dialog stays open with what was entered, and the version is read again',
  async (_answer, answer, message) => {
    const user = userEvent.setup();
    changing = async () => answer();
    await renderPage();

    await user.click(screen.getByRole('button', { name: t('editorial.chapters.add') }));
    const dialog = within(await screen.findByRole('dialog', { name: t('editorial.chapterForm.addTitle') }));
    await fillAndSave(user, dialog, { title: 'Lagerung', text: 'Kühl und trocken.' });

    await waitFor(() => expect(notify.failure).toHaveBeenCalledWith(message()));
    expect(notify.failure).toHaveBeenCalledOnce();
    expect(notify.success).not.toHaveBeenCalled();
    expect(screen.getByRole('dialog', { name: t('editorial.chapterForm.addTitle') })).toBeInTheDocument();
    expect(dialog.getByRole('textbox', { name: t('editorial.chapterForm.title') })).toHaveValue('Lagerung');
    expect(dialog.getByRole('textbox', { name: t('editorial.chapterForm.text') })).toHaveValue('Kühl und trocken.');
    await waitFor(() => expect(reads().length).toBeGreaterThan(1));
  },
);

test('a save that failed in German names the chapter', async () => {
  const user = userEvent.setup();
  changing = async () => new Response('', { status: 500 });
  await renderPage();

  await user.click(screen.getByRole('button', { name: t('editorial.chapters.add') }));
  const dialog = within(await screen.findByRole('dialog', { name: t('editorial.chapterForm.addTitle') }));
  await fillAndSave(user, dialog, { title: 'Lagerung', text: 'Kühl und trocken.' });

  await waitFor(() => expect(notify.failure).toHaveBeenCalledOnce());
  expect(notify.failure.mock.calls[0]?.[0]).toMatch(/^Das Kapitel „Lagerung. konnte nicht gespeichert werden\.$/);
});

describe('deleting a chapter', () => {
  test('asks first, naming the chapter and the questions deleted with it, then deletes it and reports it', async () => {
    const user = userEvent.setup();
    await renderPage();

    const dialog = await openConfirmation(user, introduction);
    expect(
      dialog.getByText(t('editorial.chapters.removeConfirm.text', { title: introduction.title })),
    ).toBeInTheDocument();
    expect(dialog.getByText(t('editorial.chapters.removeConfirm.questions', { count: 2 }, 2))).toBeInTheDocument();
    expect(changes()).toEqual([]);
    await user.click(dialog.getByRole('button', { name: t('editorial.chapters.removeConfirm.confirm') }));

    await waitFor(() =>
      expect(notify.success).toHaveBeenCalledWith(t('editorial.chapters.removed', { title: introduction.title })),
    );
    expect(changes()).toEqual([
      {
        method: 'DELETE',
        path: `/editorial/course-versions/${versionId}/chapters/${introduction.id}`,
        body: undefined,
      },
    ]);
    await waitFor(() => expect(confirmation()).toBeNull());
    await waitFor(() => expect(reads().length).toBeGreaterThan(1));
  });

  test('asks in German, a question in the singular and several in the plural', async () => {
    const user = userEvent.setup();
    await renderPage();

    let dialog = await openConfirmation(user, introduction);
    expect(confirmation()).toHaveAccessibleName('Kapitel löschen?');
    expect(dialog.getByText(/^„Einleitung. wird gelöscht\.$/)).toBeInTheDocument();
    expect(dialog.getByText('Mit dem Kapitel werden 2 Fragen gelöscht, die sich darauf beziehen.')).toBeInTheDocument();
    expect(dialog.getByRole('button', { name: 'Löschen' })).toBeInTheDocument();
    await user.click(dialog.getByRole('button', { name: 'Abbrechen' }));
    await waitFor(() => expect(confirmation()).toBeNull());

    dialog = await openConfirmation(user, handwashing);
    expect(dialog.getByText('Mit dem Kapitel wird eine Frage gelöscht, die sich darauf bezieht.')).toBeInTheDocument();
  });

  test('a chapter no question is about deletes no question', async () => {
    const user = userEvent.setup();
    await renderPage();

    const dialog = await openConfirmation(user, handout);

    expect(dialog.getByText(t('editorial.chapters.removeConfirm.text', { title: handout.title }))).toBeInTheDocument();
    expect(dialog.queryByText(/Frage/)).not.toBeInTheDocument();
  });

  test('cancelled, deletes nothing', async () => {
    const user = userEvent.setup();
    await renderPage();

    const dialog = await openConfirmation(user, introduction);
    await user.click(dialog.getByRole('button', { name: t('editorial.cancel') }));

    await waitFor(() => expect(confirmation()).toBeNull());
    expect(changes()).toEqual([]);
    expect(notify.success).not.toHaveBeenCalled();
  });

  test.each([
    ['a 409 of the module', notADraft, () => 'Die Version ist kein Entwurf mehr und kann nicht geändert werden.'],
    [
      'a 500',
      () => new Response('', { status: 500 }),
      () => t('editorial.chapters.removeFailed', { title: introduction.title }),
    ],
  ])('refused with %s, is reported and the version is read again', async (_answer, answer, message) => {
    const user = userEvent.setup();
    changing = async () => answer();
    await renderPage();

    const dialog = await openConfirmation(user, introduction);
    await user.click(dialog.getByRole('button', { name: t('editorial.chapters.removeConfirm.confirm') }));

    await waitFor(() => expect(notify.failure).toHaveBeenCalledWith(message()));
    expect(notify.success).not.toHaveBeenCalled();
    await waitFor(() => expect(reads().length).toBeGreaterThan(1));
  });

  // Someone submitted the version for review meanwhile: the host refuses with NotADraft, and the page, reading the
  // version again, shows it read-only.
  test('refused because the version is no draft any more, shows the version read-only', async () => {
    const user = userEvent.setup();
    changing = async () => {
      version = versionOf('InReview');
      return notADraft();
    };
    await renderPage();

    const dialog = await openConfirmation(user, introduction);
    await user.click(dialog.getByRole('button', { name: t('editorial.chapters.removeConfirm.confirm') }));

    expect(await screen.findByText(t('editorial.version.readOnly.InReview'))).toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: t('editorial.chapters.remove', { title: introduction.title }) }),
    ).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: t('editorial.chapters.add') })).not.toBeInTheDocument();
  });
});

// Reordering (T8): the move buttons and dragging a chapter by its handle both send every chapter's id in the new order
// (PUT .../chapters/order). The list shows the new order before the host answers, and the previous one again if it
// refuses.

/** The chapters' titles in the order the list shows them. */
const shownOrder = () =>
  within(chapterList())
    .getAllByRole('listitem')
    .map(
      (listItem) =>
        [introduction, handout, handwashing].find((each) => within(listItem).queryByText(each.title) !== null)?.title,
    );

/** The host's answer to the reorder, held until the test lets it go. */
function holdReorder() {
  let answer: (response: Response) => void = () => undefined;
  const response = new Promise<Response>((resolve) => {
    answer = resolve;
  });
  changing = (method, path) =>
    method === 'PUT' && path.endsWith('/chapters/order')
      ? response
      : Promise.resolve(new Response(null, { status: 204 }));
  return answer;
}

describe('reordering the chapters', () => {
  test('a move button sends the new order and the list shows it before the host answers', async () => {
    const user = userEvent.setup();
    const answer = holdReorder();
    await renderPage();

    await user.click(
      screen.getByRole('button', { name: t('editorial.chapters.moveUp', { title: handwashing.title }) }),
    );

    await waitFor(() => expect(shownOrder()).toEqual([introduction.title, handwashing.title, handout.title]));
    expect(changes()).toEqual([
      {
        method: 'PUT',
        path: `/editorial/course-versions/${versionId}/chapters/order`,
        body: { chapters: [introduction.id, handwashing.id, handout.id] },
      },
    ]);
    expect(
      await screen.findByText(t('editorial.chapters.moved', { title: handwashing.title, position: 2, count: 3 })),
    ).toBeInTheDocument();
    // The button pressed keeps the focus once its chapter has moved.
    expect(
      screen.getByRole('button', { name: t('editorial.chapters.moveUp', { title: handwashing.title }) }),
    ).toHaveFocus();

    version = versionOf('Draft', [introduction, { ...handwashing, position: 2 }, { ...handout, position: 3 }]);
    answer(Response.json({}));

    await waitFor(() => expect(reads().length).toBeGreaterThan(1));
    expect(shownOrder()).toEqual([introduction.title, handwashing.title, handout.title]);
    expect(notify.failure).not.toHaveBeenCalled();
  });

  test('dragging a chapter by its handle onto another sends the new order', async () => {
    await renderPage();

    const transfer = { setData: () => undefined, getData: () => '', dropEffect: 'none', effectAllowed: 'all' };
    const target = chapterItem(introduction) as HTMLElement;
    await fireEvent.dragStart(
      within(chapterItem(handwashing) as HTMLElement).getByTitle(t('editorial.chapters.dragHint')),
      { dataTransfer: transfer },
    );
    await fireEvent.dragOver(target, { dataTransfer: transfer });
    await fireEvent.drop(target, { dataTransfer: transfer });

    await waitFor(() =>
      expect(changes()).toEqual([
        {
          method: 'PUT',
          path: `/editorial/course-versions/${versionId}/chapters/order`,
          body: { chapters: [handwashing.id, introduction.id, handout.id] },
        },
      ]),
    );
    await waitFor(() => expect(shownOrder()).toEqual([handwashing.title, introduction.title, handout.title]));
  });

  test.each([
    [
      'a 409 of the module',
      () =>
        new Response(
          JSON.stringify({
            title: 'Conflict',
            status: 409,
            detail: 'Die Reihenfolge nennt nicht jedes Kapitel der Version genau einmal.',
            code: 'editorial.order-does-not-match-chapters',
          }),
          { status: 409, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      () => 'Die Reihenfolge nennt nicht jedes Kapitel der Version genau einmal.',
    ],
    [
      'a 500',
      () => new Response('', { status: 500 }),
      () => 'Die Reihenfolge konnte nicht gespeichert werden. Die vorherige Reihenfolge ist wiederhergestellt.',
    ],
  ])('an order the host refuses with %s is undone on the page and reported', async (_answer, refusal, message) => {
    const user = userEvent.setup();
    const answer = holdReorder();
    await renderPage();

    await user.click(
      screen.getByRole('button', { name: t('editorial.chapters.moveDown', { title: introduction.title }) }),
    );
    await waitFor(() => expect(shownOrder()).toEqual([handout.title, introduction.title, handwashing.title]));
    answer(refusal());

    await waitFor(() => expect(notify.failure).toHaveBeenCalledWith(message()));
    await waitFor(() => expect(shownOrder()).toEqual([introduction.title, handout.title, handwashing.title]));
    await waitFor(() => expect(reads().length).toBeGreaterThan(1));
  });
});
