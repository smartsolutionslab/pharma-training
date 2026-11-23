import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { cleanup, render, screen, waitFor, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest';
import { createMemoryHistory } from 'vue-router';
import type { EditorialCourseVersionDto } from '@/api/generated';
import { t } from '@/i18n';
import { createAppRouter } from '@/router';
import CourseTestPage from './CourseTestPage.vue';
import { approvedAi, handWritten, unreviewedAi, versionId, versionOf } from './courseTestFixtures';

// Where focus goes after an action of the test page (#25, review round 2, S4): the button pressed is gone once the
// version is read again, so the page puts focus where the editor goes on, never on the body. After an approval, on the
// next unreviewed question's approve button, else on the approved question's card; after a deletion, on the next card,
// else on the questions' heading; when the AI draft starts, on its running status.

const notify = vi.hoisted(() => ({ success: vi.fn(), failure: vi.fn() }));
vi.mock('@/lib/notify', () => ({ notify }));

const host = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>();
vi.stubGlobal('fetch', host);

const versionPath = `/api/editorial/course-versions/${versionId}`;

let version: EditorialCourseVersionDto;
let drafting: () => Promise<Response>;

beforeEach(() => {
  version = versionOf('Draft');
  drafting = () => new Promise<Response>(() => undefined);
  // The host as it would answer: an approval marks the question approved, a deletion takes it away.
  host.mockImplementation(async (input, init) => {
    const url = new URL(String(input), 'http://localhost');
    const method = init?.method ?? 'GET';
    if (method === 'GET' && url.pathname === versionPath) {
      return Response.json(version);
    }
    if (method === 'POST' && url.pathname === `${versionPath}/test-draft`) {
      return drafting();
    }
    const approve = new RegExp(`^${versionPath}/questions/([0-9a-f-]+)/approve$`).exec(url.pathname);
    if (method === 'POST' && approve?.[1] !== undefined) {
      const approvedId = approve[1];
      version = {
        ...version,
        questions: version.questions.map((question) =>
          question.id === approvedId
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
    }
    const removed = new RegExp(`^${versionPath}/questions/([0-9a-f-]+)$`).exec(url.pathname);
    if (method === 'DELETE' && removed?.[1] !== undefined) {
      const removedId = removed[1];
      version = { ...version, questions: version.questions.filter((question) => question.id !== removedId) };
      return Response.json({ value: null });
    }
    return new Response('', { status: 404 });
  });
  notify.success.mockReset();
  notify.failure.mockReset();
});
afterEach(() => host.mockReset());
afterEach(cleanup);

async function renderPage() {
  const router = createAppRouter(createMemoryHistory());
  await router.push(`/editorial/versions/${versionId}/test`);
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  const rendered = render(CourseTestPage, { global: { plugins: [router, [VueQueryPlugin, { queryClient }]] } });
  await screen.findByRole('heading', { level: 1, name: t('editorial.test.heading', { title: version.title }) });
  return rendered;
}

type User = ReturnType<typeof userEvent.setup>;

const approveButton = (position: number) =>
  screen.queryByRole('button', { name: t('editorial.test.questions.approve', { position }) });
const questionList = () => screen.getByRole('list', { name: t('editorial.test.questions.title') });
/** The card of the question with this text: the question list's own item around it. */
const cardOf = (text: string) =>
  [...questionList().querySelectorAll(':scope > li')].find((card) => card.textContent?.includes(text)) ?? null;
const focused = () => document.activeElement;

describe('after an approval', () => {
  test('focus goes to the next unreviewed question’s approve button', async () => {
    const user = userEvent.setup();
    await renderPage();

    await user.click(approveButton(2) ?? document.body);

    await waitFor(() => expect(approveButton(2)).toBeNull());
    await waitFor(() => expect(focused()).toBe(approveButton(3)));
    expect(focused()).not.toBe(document.body);
  });

  test('with no unreviewed question left, focus goes to the approved question’s card', async () => {
    const user = userEvent.setup();
    version = versionOf('Draft', { questions: [approvedAi, unreviewedAi] });
    await renderPage();

    await user.click(approveButton(2) ?? document.body);

    await waitFor(() => expect(approveButton(2)).toBeNull());
    await waitFor(() => expect(focused()).toBe(cardOf(unreviewedAi.text)));
    expect(cardOf(unreviewedAi.text)).toHaveAttribute('tabindex', '-1');
    expect(focused()).not.toBe(document.body);
  });

  test('in review, focus goes to the next unreviewed question’s approve button too', async () => {
    const user = userEvent.setup();
    version = versionOf('InReview');
    await renderPage();

    await user.click(approveButton(2) ?? document.body);

    await waitFor(() => expect(approveButton(2)).toBeNull());
    await waitFor(() => expect(focused()).toBe(approveButton(3)));
  });
});

describe('after a deletion', () => {
  const confirmation = () =>
    screen.queryByRole('alertdialog', { name: t('editorial.test.questions.removeConfirm.title') }) ??
    screen.queryByRole('dialog', { name: t('editorial.test.questions.removeConfirm.title') });

  async function remove(user: User, position: number) {
    await user.click(screen.getByRole('button', { name: t('editorial.test.questions.remove', { position }) }));
    await waitFor(() => expect(confirmation()).not.toBeNull());
    await user.click(
      within(confirmation() ?? document.body).getByRole('button', {
        name: t('editorial.test.questions.removeConfirm.confirm'),
      }),
    );
    await waitFor(() => expect(confirmation()).toBeNull());
  }

  test('focus goes to the next question’s card', async () => {
    const user = userEvent.setup();
    await renderPage();

    await remove(user, 2);

    await waitFor(() => expect(screen.queryByText(unreviewedAi.text)).not.toBeInTheDocument());
    await waitFor(() => expect(focused()).toBe(cardOf(handWritten.text)));
    expect(cardOf(handWritten.text)).toHaveAttribute('tabindex', '-1');
    expect(focused()).not.toBe(document.body);
  });

  test('with no question left, focus goes to the questions’ heading', async () => {
    const user = userEvent.setup();
    version = versionOf('Draft', { questions: [handWritten] });
    await renderPage();

    await remove(user, 3);

    await waitFor(() => expect(screen.queryByText(handWritten.text)).not.toBeInTheDocument());
    await waitFor(() =>
      expect(focused()).toBe(screen.getByRole('heading', { level: 2, name: t('editorial.test.questions.title') })),
    );
    expect(focused()).not.toBe(document.body);
  });
});

test('when the AI draft starts, focus goes to its running status', async () => {
  const user = userEvent.setup();
  await renderPage();

  await user.click(screen.getByRole('button', { name: t('editorial.test.draft.start') }));
  const dialog = await waitFor(() => {
    const found =
      screen.queryByRole('alertdialog', { name: t('editorial.test.draft.confirm.title') }) ??
      screen.queryByRole('dialog', { name: t('editorial.test.draft.confirm.title') });
    expect(found).not.toBeNull();
    return within(found ?? document.body);
  });
  await user.click(dialog.getByRole('button', { name: t('editorial.test.draft.confirm.submit') }));

  const running = await screen.findByText(t('editorial.test.draft.running'));
  await waitFor(() =>
    expect(
      screen.queryByRole('dialog', { name: t('editorial.test.draft.confirm.title') }) ??
        screen.queryByRole('alertdialog', { name: t('editorial.test.draft.confirm.title') }),
    ).toBeNull(),
  );
  await waitFor(() => {
    expect(focused()).not.toBe(document.body);
    expect(focused()?.contains(running)).toBe(true);
  });
  expect(focused()?.closest('[role="status"]') ?? focused()?.querySelector('[role="status"]')).not.toBeNull();
});
