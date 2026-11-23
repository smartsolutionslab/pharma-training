import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { cleanup, render, screen, waitFor, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import { createMemoryHistory } from 'vue-router';
import type { EditorialCourseVersionDto, EditorialCourseVersionStatus } from '@/api/generated';
import { setLanguage, t } from '@/i18n';
import { createAppRouter } from '@/router';
import CourseTestPage from './CourseTestPage.vue';
import { handWritten, refusal, unreviewedAi, versionId, versionOf } from './courseTestFixtures';

// Approving a question (#25, S6 and T6): an unreviewed question in a draft or in review is approved with its own
// button, which shows Freigeben and is named after the question; the host checks the options (422) and refuses a second
// approval (409 question-already-approved, PR #177), and its words are shown. The version is read again either way.

const notify = vi.hoisted(() => ({ success: vi.fn(), failure: vi.fn() }));
vi.mock('@/lib/notify', () => ({ notify }));

const host = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>();
vi.stubGlobal('fetch', host);

let version: EditorialCourseVersionDto;
let approving: (questionId: string) => Promise<Response>;

const versionPath = `/api/editorial/course-versions/${versionId}`;

beforeEach(() => {
  version = versionOf('Draft');
  // By default the host approves the question, as it would.
  approving = async (questionId) => {
    version = {
      ...version,
      questions: version.questions.map((question) =>
        question.id === questionId
          ? {
              ...question,
              review: 'Approved' as const,
              reviewedBy: '0198c1a0-0000-7000-8000-0000000000d8',
              reviewedAt: '2026-11-22T10:00:00Z',
              approvedByName: 'Emma Editor',
              approvedAt: '2026-11-22T10:00:00Z',
            }
          : question,
      ),
    };
    return Response.json({ value: null });
  };
  host.mockImplementation(async (input, init) => {
    const url = new URL(String(input), 'http://localhost');
    const method = init?.method ?? 'GET';
    if (method === 'GET' && url.pathname === versionPath) {
      return Response.json(version);
    }
    const approve = new RegExp(`^${versionPath}/questions/([0-9a-f-]+)/approve$`).exec(url.pathname);
    if (method === 'POST' && approve?.[1] !== undefined) {
      return approving(approve[1]);
    }
    return new Response('', { status: 404 });
  });
  notify.success.mockReset();
  notify.failure.mockReset();
});
afterEach(() => host.mockReset());
afterEach(cleanup);

const changes = () =>
  host.mock.calls
    .filter(([, init]) => (init?.method ?? 'GET') !== 'GET')
    .map(([input, init]) => ({
      method: init?.method,
      path: new URL(String(input), 'http://localhost').pathname.replace(/^\/api/, ''),
    }));
const reads = () => host.mock.calls.filter(([, init]) => (init?.method ?? 'GET') === 'GET');

async function renderPage() {
  const router = createAppRouter(createMemoryHistory());
  await router.push(`/editorial/versions/${versionId}/test`);
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  const rendered = render(CourseTestPage, { global: { plugins: [router, [VueQueryPlugin, { queryClient }]] } });
  await screen.findByRole('heading', { level: 1, name: t('editorial.test.heading', { title: version.title }) });
  return rendered;
}

const approveButton = (position: number) =>
  screen.queryByRole('button', { name: t('editorial.test.questions.approve', { position }) });
const questionList = () => screen.getByRole('list', { name: t('editorial.test.questions.title') });
const card = (index: number) =>
  ([...questionList().querySelectorAll(':scope > li')][index] as HTMLElement | undefined) ?? document.body;

test('an unreviewed question in a draft is approved and the version reloaded', async () => {
  const user = userEvent.setup();
  await renderPage();

  const button = approveButton(2);
  expect(button).not.toBeNull();
  expect(button).toHaveTextContent(t('editorial.test.questions.approveLabel'));
  await user.click(button ?? document.body);

  await waitFor(() =>
    expect(notify.success).toHaveBeenCalledWith(t('editorial.test.questions.approved', { position: 2 })),
  );
  expect(notify.success).toHaveBeenCalledWith('Frage 2 ist freigegeben.');
  expect(changes()).toEqual([
    { method: 'POST', path: `/editorial/course-versions/${versionId}/questions/${unreviewedAi.id}/approve` },
  ]);
  await waitFor(() => expect(reads().length).toBeGreaterThan(1));
  // Read again, the question is approved and offers no approve.
  await waitFor(() => expect(within(card(1)).getByText('Geprüft')).toBeInTheDocument());
  expect(approveButton(2)).toBeNull();
  expect(screen.getByText('2 von 10 Fragen geprüft')).toBeInTheDocument();
  expect(notify.failure).not.toHaveBeenCalled();
});

test('the approve button shows Freigeben and is named Frage 2 freigeben, in English Approve question 2', async () => {
  await renderPage();

  const button = screen.getByRole('button', { name: 'Frage 2 freigeben' });
  expect(button).toHaveTextContent('Freigeben');
  expect(within(card(1)).getByRole('button', { name: 'Frage 2 freigeben' })).toBe(button);
  expect(within(card(2)).getByRole('button', { name: 'Frage 3 freigeben' })).toBeInTheDocument();

  setLanguage('en-GB', { remember: false });

  const english = await screen.findByRole('button', { name: 'Approve question 2' });
  expect(english).toHaveTextContent('Approve');
});

test('a question in review can be approved', async () => {
  const user = userEvent.setup();
  version = versionOf('InReview');
  await renderPage();

  await user.click(screen.getByRole('button', { name: 'Frage 3 freigeben' }));

  await waitFor(() => expect(notify.success).toHaveBeenCalledWith('Frage 3 ist freigegeben.'));
  expect(changes()).toEqual([
    { method: 'POST', path: `/editorial/course-versions/${versionId}/questions/${handWritten.id}/approve` },
  ]);
});

test('an approved question offers no approve', async () => {
  await renderPage();

  expect(approveButton(2)).not.toBeNull();
  expect(approveButton(1)).toBeNull();
  expect(within(card(0)).queryByRole('button', { name: /freigeben$/ })).not.toBeInTheDocument();
});

test.each<EditorialCourseVersionStatus>(['Published', 'Superseded', 'Retired'])(
  'a %s version offers no approve',
  async (status) => {
    version = versionOf(status);
    await renderPage();

    for (const position of [1, 2, 3]) {
      expect(approveButton(position)).toBeNull();
    }
    expect(screen.queryByRole('button', { name: /^Frage \d freigeben$/ })).not.toBeInTheDocument();
    // The questions are there, still unreviewed.
    expect(within(card(1)).getByText('Ungeprüft')).toBeInTheDocument();
  },
);

test.each([
  [
    '422 too-few-options',
    () => refusal(422, 'editorial.too-few-options', 'Eine Frage braucht mindestens zwei Antwortoptionen.'),
  ],
  [
    '422 single-choice-one-correct',
    () =>
      refusal(422, 'editorial.single-choice-one-correct', 'Bei Single Choice ist genau eine Antwortoption richtig.'),
  ],
  [
    '409 question-already-approved',
    () => refusal(409, 'editorial.question-already-approved', 'Die Frage ist bereits freigegeben.'),
  ],
])("a %s reports the host's message and reloads the version", async (_answer, answer) => {
  const user = userEvent.setup();
  const refused = answer();
  const detail = ((await refused.clone().json()) as { detail: string }).detail;
  approving = async () => refused;
  await renderPage();

  await user.click(screen.getByRole('button', { name: t('editorial.test.questions.approve', { position: 2 }) }));

  await waitFor(() => expect(notify.failure).toHaveBeenCalledWith(detail));
  expect(notify.failure).toHaveBeenCalledOnce();
  expect(notify.success).not.toHaveBeenCalled();
  await waitFor(() => expect(reads().length).toBeGreaterThan(1));
});

test('an approval that failed without words of the host says which question, in German', async () => {
  const user = userEvent.setup();
  approving = async () => new Response('', { status: 500 });
  await renderPage();

  await user.click(screen.getByRole('button', { name: t('editorial.test.questions.approve', { position: 2 }) }));

  await waitFor(() =>
    expect(notify.failure).toHaveBeenCalledWith(t('editorial.test.questions.approveFailed', { position: 2 })),
  );
  expect(notify.failure).toHaveBeenCalledWith('Frage 2 konnte nicht freigegeben werden.');
  await waitFor(() => expect(reads().length).toBeGreaterThan(1));
});

// In review a question can still be approved, but not changed (#25, review round 2): the page says so, not only that
// the version cannot be edited.
test('a version in review says its questions can still be approved but not changed', async () => {
  version = versionOf('InReview');
  await renderPage();

  expect(screen.getByText(t('editorial.test.readOnly.InReviewApprove'))).toBeInTheDocument();
  expect(
    screen.getByText('Diese Version ist in Prüfung. Fragen können noch freigegeben, aber nicht geändert werden.'),
  ).toBeInTheDocument();

  setLanguage('en-GB', { remember: false });

  expect(
    await screen.findByText('This version is in review. Questions can still be approved but not changed.'),
  ).toBeInTheDocument();
});

test.each<EditorialCourseVersionStatus>(['Draft', 'Published', 'Superseded', 'Retired'])(
  'a %s version does not say approving is still possible',
  async (status) => {
    version = versionOf(status);
    await renderPage();

    expect(
      screen.queryByText('Diese Version ist in Prüfung. Fragen können noch freigegeben, aber nicht geändert werden.'),
    ).not.toBeInTheDocument();
  },
);
