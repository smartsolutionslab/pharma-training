import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { cleanup, render, screen, waitFor, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import { createMemoryHistory } from 'vue-router';
import type { EditorialCourseVersionSummaryDto } from '@/api/generated';
import { setLanguage, t } from '@/i18n';
import { createAppRouter } from '@/router';
import EditorialPage from './EditorialPage.vue';

const notify = vi.hoisted(() => ({ success: vi.fn(), failure: vi.fn() }));
vi.mock('@/lib/notify', () => ({ notify }));

// The host, as GET /editorial/course-versions reaches it through the generated client: each status answers the versions
// the test gives it, a page of 20 at a time, and the total the test says (the whole list by default).
const host = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>();
vi.stubGlobal('fetch', host);

type Status = 'Draft' | 'InReview' | 'Published';

const hygiene: EditorialCourseVersionSummaryDto = {
  id: '0198c1a0-0000-7000-8000-0000000000c1',
  courseId: '0198c1a0-0000-7000-8000-0000000000d1',
  number: 2,
  title: 'Hygiene in der Apotheke',
  status: 'Draft',
};
const labelling: EditorialCourseVersionSummaryDto = {
  id: '0198c1a0-0000-7000-8000-0000000000c2',
  courseId: '0198c1a0-0000-7000-8000-0000000000d2',
  number: 1,
  title: 'Kennzeichnung von Rezepturen',
  status: 'InReview',
};

let lists: Record<Status, EditorialCourseVersionSummaryDto[]>;
let totals: Partial<Record<Status, number>>;
let listing: (status: string | null) => Promise<Response>;

const listed = async (status: string | null) => {
  const items = lists[status as Status] ?? [];
  return Response.json({ items, totalCount: totals[status as Status] ?? items.length, page: 1, pageSize: 20 });
};

beforeEach(() => {
  lists = { Draft: [hygiene], InReview: [labelling], Published: [] };
  totals = {};
  listing = listed;
  host.mockImplementation(async (input, init) => {
    const url = new URL(String(input), 'http://localhost');
    if ((init?.method ?? 'GET') === 'GET' && url.pathname === '/api/editorial/course-versions') {
      return listing(url.searchParams.get('status'));
    }
    return new Response('', { status: 404 });
  });
  notify.success.mockReset();
  notify.failure.mockReset();
});
afterEach(() => host.mockReset());
// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(cleanup);

/** The inbox lists the host was asked for, by their query. */
const listsAsked = () =>
  host.mock.calls
    .map(([input]) => new URL(String(input), 'http://localhost'))
    .filter((url) => url.pathname === '/api/editorial/course-versions')
    .map((url) => Object.fromEntries(url.searchParams));

/** The page at its address, with a query client of its own that does not retry, so a failure shows at once. */
async function renderAt(path = '/editorial') {
  const router = createAppRouter(createMemoryHistory());
  await router.push(path);
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  render(EditorialPage, { global: { plugins: [router, [VueQueryPlugin, { queryClient }]] } });
  return { router, queryClient };
}

const tabs = () => screen.getByRole('tablist', { name: t('editorial.inbox.statuses') });
const tab = (status: Status) => within(tabs()).getByRole('tab', { name: t(`glossary.courseVersionStatus.${status}`) });

test('the page is headed by the module, names its inbox and offers the three statuses, drafts first', async () => {
  await renderAt();

  expect(screen.getByRole('heading', { level: 1, name: t('modules.editorial.title') })).toBeInTheDocument();
  expect(screen.getByRole('heading', { level: 2, name: t('editorial.inbox.title') })).toBeInTheDocument();
  expect(
    within(tabs())
      .getAllByRole('tab')
      .map((each) => each.textContent.trim()),
  ).toEqual([
    t('glossary.courseVersionStatus.Draft'),
    t('glossary.courseVersionStatus.InReview'),
    t('glossary.courseVersionStatus.Published'),
  ]);
  expect(tab('Draft')).toHaveAttribute('aria-selected', 'true');
  expect(tab('InReview')).toHaveAttribute('aria-selected', 'false');
  expect(screen.getByRole('button', { name: t('editorial.course.new') })).toBeInTheDocument();
});

// The generated web sample is dropped from the editorial module (spec 2.2): no samples, no form for them and no Manage
// probe, whose manager role an editor does not hold.
test('the page shows no samples and no Manage section', async () => {
  await renderAt();

  await screen.findByRole('tablist', { name: t('editorial.inbox.statuses') });
  expect(screen.queryByRole('heading', { name: t('samples.title') })).not.toBeInTheDocument();
  expect(screen.queryByRole('heading', { name: t('samples.manage') })).not.toBeInTheDocument();
  expect(screen.queryByRole('form', { name: t('samples.form.label') })).not.toBeInTheDocument();
  expect(host.mock.calls.map(([input]) => String(input))).not.toContainEqual(
    expect.stringContaining('/editorial/samples'),
  );
  expect(host.mock.calls.map(([input]) => String(input))).not.toContainEqual(
    expect.stringContaining('/editorial/permissions'),
  );
});

test('the drafts tab lists the versions by title, each a link to its page, with its number and status', async () => {
  await renderAt();

  const link = await screen.findByRole('link', { name: hygiene.title });
  expect(link).toHaveAttribute('href', `/editorial/versions/${hygiene.id}`);
  expect(screen.getByRole('columnheader', { name: t('editorial.inbox.columns.title') })).toBeInTheDocument();
  expect(screen.getByRole('columnheader', { name: t('editorial.inbox.columns.number') })).toBeInTheDocument();
  expect(screen.getByRole('columnheader', { name: t('editorial.inbox.columns.status') })).toBeInTheDocument();
  const row = within(link.closest('tr') ?? document.body);
  expect(row.getByRole('cell', { name: '2' })).toBeInTheDocument();
  expect(row.getByRole('cell', { name: t('glossary.courseVersionStatus.Draft') })).toBeInTheDocument();
  expect(listsAsked()).toEqual([{ status: 'Draft', page: '1', pageSize: '20' }]);
});

test('choosing "In Prüfung" asks for the versions in review and puts the status in the address', async () => {
  const user = userEvent.setup();
  const { router } = await renderAt('/editorial?status=Draft&page=2');
  await screen.findByRole('tablist', { name: t('editorial.inbox.statuses') });

  await user.click(tab('InReview'));

  await waitFor(() => expect(router.currentRoute.value.query.status).toBe('InReview'));
  // Switching a tab starts at its first page.
  expect(router.currentRoute.value.query.page ?? '1').toBe('1');
  expect(tab('InReview')).toHaveAttribute('aria-selected', 'true');
  expect(await screen.findByRole('link', { name: labelling.title })).toHaveAttribute(
    'href',
    `/editorial/versions/${labelling.id}`,
  );
  expect(screen.queryByRole('link', { name: hygiene.title })).not.toBeInTheDocument();
  expect(listsAsked()).toContainEqual({ status: 'InReview', page: '1', pageSize: '20' });
});

test('the address chooses the tab and the page', async () => {
  await renderAt('/editorial?status=Published&page=2');

  await waitFor(() => expect(tab('Published')).toHaveAttribute('aria-selected', 'true'));
  await waitFor(() => expect(listsAsked()).toEqual([{ status: 'Published', page: '2', pageSize: '20' }]));
});

test.each([['/editorial?status=Bogus'], ['/editorial?status=Superseded&page=abc'], ['/editorial?status=Draft&page=0']])(
  'at %s the inbox falls back to the first page of the drafts, and the host never sees the invalid value',
  async (path) => {
    await renderAt(path);

    await waitFor(() => expect(tab('Draft')).toHaveAttribute('aria-selected', 'true'));
    expect(await screen.findByRole('link', { name: hygiene.title })).toBeInTheDocument();
    expect(listsAsked()).toEqual([{ status: 'Draft', page: '1', pageSize: '20' }]);
  },
);

test("the pager's next page puts the page in the address and asks the host for it", async () => {
  const user = userEvent.setup();
  totals = { Draft: 45 };
  const { router } = await renderAt();
  await screen.findByRole('link', { name: hygiene.title });

  await user.click(screen.getByRole('button', { name: t('dataTable.next') }));

  await waitFor(() => expect(router.currentRoute.value.query.page).toBe('2'));
  expect(router.currentRoute.value.query.status ?? 'Draft').toBe('Draft');
  await waitFor(() => expect(listsAsked()).toContainEqual({ status: 'Draft', page: '2', pageSize: '20' }));
});

test('while the versions load the table is busy and shows skeleton rows', async () => {
  listing = () => new Promise<Response>(() => undefined);

  await renderAt();

  const table = await screen.findByRole('table', { name: t('dataTable.loading'), busy: true });
  expect(within(table).getByRole('columnheader', { name: t('editorial.inbox.columns.title') })).toBeInTheDocument();
  expect(table.querySelector('[data-slot="skeleton"]')).not.toBeNull();
  expect(listsAsked()).toEqual([{ status: 'Draft', page: '1', pageSize: '20' }]);
  expect(screen.queryByText(t('editorial.inbox.empty.Draft'))).not.toBeInTheDocument();
});

test('without drafts the inbox says so, and its action opens the form that starts a course', async () => {
  const user = userEvent.setup();
  lists.Draft = [];
  await renderAt();

  const sentence = await screen.findByText(t('editorial.inbox.empty.Draft'));
  const emptyState = within(sentence.closest('[data-slot="empty-state"]') as HTMLElement);
  await user.click(emptyState.getByRole('button', { name: t('editorial.course.new') }));

  expect(await screen.findByRole('dialog', { name: t('editorial.course.form.label') })).toBeInTheDocument();
});

test.each([['InReview' as const], ['Published' as const]])(
  'without versions in %s the inbox says so, with no action',
  async (status) => {
    lists[status] = [];
    await renderAt(`/editorial?status=${status}`);

    const sentence = await screen.findByText(t(`editorial.inbox.empty.${status}`));
    const emptyState = within(sentence.closest('[data-slot="empty-state"]') as HTMLElement);
    expect(emptyState.queryByRole('button')).not.toBeInTheDocument();
  },
);

test('an inbox the host cannot give is reported', async () => {
  listing = async () => new Response('', { status: 500 });

  await renderAt();

  expect(await screen.findByRole('alert')).toHaveTextContent(t('editorial.inbox.loadFailed'));
  expect(screen.queryByText(t('editorial.inbox.empty.Draft'))).not.toBeInTheDocument();
});

// The German texts themselves, not only their keys: a key that resolved to the wrong text would pass the t() queries.
test('the inbox speaks German by default', async () => {
  lists.Draft = [];
  await renderAt();

  expect(screen.getByRole('heading', { level: 2, name: 'Eingang' })).toBeInTheDocument();
  const statuses = screen.getByRole('tablist', { name: 'Status' });
  expect(
    within(statuses)
      .getAllByRole('tab')
      .map((each) => each.textContent.trim()),
  ).toEqual(['Entwurf', 'In Prüfung', 'Veröffentlicht']);
  expect(await screen.findByText('Keine Entwürfe.')).toBeVisible();
  expect(screen.getAllByRole('button', { name: 'Neue Schulung' }).length).toBeGreaterThan(0);
});

test('the inbox names its columns and a failure in German', async () => {
  await renderAt();

  expect(await screen.findByRole('columnheader', { name: 'Schulung' })).toBeInTheDocument();
  expect(screen.getByRole('columnheader', { name: 'Version' })).toBeInTheDocument();
  expect(screen.getByRole('columnheader', { name: 'Status' })).toBeInTheDocument();
  cleanup();

  listing = async () => new Response('', { status: 500 });
  await renderAt();

  expect(await screen.findByRole('alert')).toHaveTextContent('Der Eingang konnte nicht geladen werden.');
});

test('switched to English, the inbox, its tabs and its empty state speak English', async () => {
  lists.Published = [];
  await renderAt('/editorial?status=Published');
  await screen.findByText(t('editorial.inbox.empty.Published'));

  setLanguage('en-GB', { remember: false });

  expect(await screen.findByRole('heading', { level: 2, name: 'Inbox' })).toBeInTheDocument();
  expect(
    within(screen.getByRole('tablist', { name: 'Status' }))
      .getAllByRole('tab')
      .map((each) => each.textContent.trim()),
  ).toEqual(['Draft', 'In review', 'Published']);
  expect(screen.getByText('No published versions.')).toBeVisible();
});
