import { QueryClient, VueQueryPlugin, type VueQueryPluginOptions } from '@tanstack/vue-query';
import { cleanup, render, screen, waitFor, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import { defineComponent, h, ref } from 'vue';
import type { EditorialCourseVersionReturnDto } from '@/api/generated';
import { i18n, setLanguage, t } from '@/i18n';
import ReturnHistory from './ReturnHistory.vue';

// The host, as GET /editorial/course-versions/{id}/returns reaches it through the generated client: each page answers
// the records the test gives it, in the host's order (newest first), with the total the test says.
const host = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>();
vi.stubGlobal('fetch', host);

const versionId = '0198c1a0-0000-7000-8000-0000000000c1';
const otherVersionId = '0198c1a0-0000-7000-8000-0000000000c2';
const editor = '0198c1a0-0000-7000-8000-0000000000e1';

const twoLines: EditorialCourseVersionReturnDto = {
  id: '0198c1a0-0000-7000-8000-0000000000a2',
  returnedBy: editor,
  returnedAt: '2026-11-21T14:05:00Z',
  reason: 'Kapitel 2 braucht eine Quelle.\nDie Tabelle in Kapitel 3 ist veraltet.',
};
const markup: EditorialCourseVersionReturnDto = {
  id: '0198c1a0-0000-7000-8000-0000000000a1',
  returnedBy: editor,
  returnedAt: '2026-11-19T08:30:00Z',
  reason: 'Frage 1: <b>30 Sekunden</b> ist zu kurz.',
};
const script: EditorialCourseVersionReturnDto = {
  id: '0198c1a0-0000-7000-8000-0000000000a0',
  returnedBy: editor,
  returnedAt: '2026-11-18T10:00:00Z',
  reason: '<img src=x onerror=alert(1)>',
};
const onPageTwo: EditorialCourseVersionReturnDto = {
  id: '0198c1a0-0000-7000-8000-00000000009f',
  returnedBy: editor,
  returnedAt: '2026-10-02T09:15:00Z',
  reason: 'Die Lernziele fehlen.',
};

/** The texts the catalogs give the history (editorial.returns.*), pinned here: the keys do not exist yet. */
const texts = {
  de: {
    title: 'Rückgaben',
    returnedAt: 'Zurückgegeben am',
    reason: 'Grund',
    empty: 'Diese Version wurde noch nie zur Überarbeitung zurückgegeben.',
    loadFailed: 'Die Rückgaben dieser Version konnten nicht geladen werden.',
  },
  en: {
    title: 'Returns',
    returnedAt: 'Returned on',
    reason: 'Reason',
    empty: 'This version has never been returned to draft.',
    loadFailed: 'The returns of this version could not be loaded.',
  },
};

let records: EditorialCourseVersionReturnDto[];
let total: number | undefined;
let answer: (id: string, page: string | null) => Promise<Response>;

const listed = async (id: string, page: string | null) =>
  Response.json({
    items: id === versionId ? (page === '2' ? [onPageTwo] : records) : [],
    totalCount: id === versionId ? (total ?? records.length) : 0,
    page: Number(page),
    pageSize: 20,
  });

beforeEach(() => {
  records = [twoLines, markup, script];
  total = undefined;
  answer = listed;
  host.mockImplementation(async (input, init) => {
    const url = new URL(String(input), 'http://localhost');
    const match = /^\/api\/editorial\/course-versions\/([^/]+)\/returns$/.exec(url.pathname);
    if ((init?.method ?? 'GET') === 'GET' && match?.[1] !== undefined) {
      return answer(match[1], url.searchParams.get('page'));
    }
    return new Response('', { status: 404 });
  });
});
afterEach(() => host.mockReset());
// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(cleanup);

/** The histories the host was asked for: the version and the query. */
const asked = () =>
  host.mock.calls
    .map(([input]) => new URL(String(input), 'http://localhost'))
    .map((url) => ({ path: url.pathname, query: Object.fromEntries(url.searchParams) }));

/** A query client of its own per render, which does not retry, so a failure shows at once. */
const queryPlugin = (): [typeof VueQueryPlugin, VueQueryPluginOptions] => [
  VueQueryPlugin,
  { queryClient: new QueryClient({ defaultOptions: { queries: { retry: false } } }) },
];

function renderHistory(id = versionId) {
  return render(ReturnHistory, { props: { versionId: id }, global: { plugins: [queryPlugin()] } });
}

/** The history's table and its data rows, the header row left out. */
const table = () => within(screen.getByRole('region', { name: texts.de.title })).getByRole('table');
const rows = () => within(table()).getAllByRole('row').slice(1);
const cells = (row: HTMLElement) => within(row).getAllByRole('cell');

const dateTime = (instant: string) => i18n.global.d(new Date(instant), 'dateTime');

test('the history is a region headed "Rückgaben", with the date and the reason as its columns', async () => {
  renderHistory();

  const region = screen.getByRole('region', { name: texts.de.title });
  expect(within(region).getByRole('heading', { level: 2, name: texts.de.title })).toBeInTheDocument();
  expect(await within(region).findByRole('columnheader', { name: texts.de.returnedAt })).toBeInTheDocument();
  expect(within(region).getByRole('columnheader', { name: texts.de.reason })).toBeInTheDocument();
});

test("it asks the host for the version's first page of returns at the default size", async () => {
  renderHistory();

  await screen.findByText(markup.reason);
  expect(asked()).toEqual([
    { path: `/api/editorial/course-versions/${versionId}/returns`, query: { page: '1', pageSize: '20' } },
  ]);
});

test("the rows are in the host's order, each with its date in the browser's time zone and its reason", async () => {
  renderHistory();

  await screen.findByText(markup.reason);
  expect(rows().map((row) => cells(row).map((cell) => cell.textContent.trim()))).toEqual([
    [dateTime(twoLines.returnedAt), twoLines.reason],
    [dateTime(markup.returnedAt), markup.reason],
    [dateTime(script.returnedAt), script.reason],
  ]);
});

test('a reason of two lines keeps both, on two lines', async () => {
  renderHistory();

  await screen.findByText(markup.reason);
  const [first] = rows();
  if (first === undefined) {
    throw new Error('The history has no rows.');
  }
  const reason = cells(first)[1];
  expect(reason?.textContent).toContain('Kapitel 2 braucht eine Quelle.\nDie Tabelle in Kapitel 3 ist veraltet.');
  // jsdom lays nothing out: the line break shows because the text keeps its newlines (Tailwind's whitespace-pre-line).
  expect(reason?.querySelector('.whitespace-pre-line')).toHaveTextContent(/Kapitel 2 braucht eine Quelle\./);
});

test('a reason holding markup shows as the text it is, and no element is made of it', async () => {
  const { container } = renderHistory();

  expect(await screen.findByText('<img src=x onerror=alert(1)>')).toBeVisible();
  expect(screen.getByText('Frage 1: <b>30 Sekunden</b> ist zu kurz.')).toBeVisible();
  expect(container.querySelector('img')).toBeNull();
  expect(container.querySelector('b')).toBeNull();
});

test('a version never returned says so, with no action', async () => {
  records = [];
  renderHistory();

  const sentence = await screen.findByText(texts.de.empty);
  const emptyState = within(sentence.closest('[data-slot="empty-state"]') as HTMLElement);
  expect(emptyState.queryByRole('button')).not.toBeInTheDocument();
});

test('while the returns load the table is busy and shows skeleton rows', async () => {
  answer = () => new Promise<Response>(() => undefined);
  renderHistory();

  const busy = await screen.findByRole('table', { name: t('dataTable.loading'), busy: true });
  expect(within(busy).getByRole('columnheader', { name: texts.de.returnedAt })).toBeInTheDocument();
  expect(busy.querySelector('[data-slot="skeleton"]')).not.toBeNull();
  expect(screen.queryByText(texts.de.empty)).not.toBeInTheDocument();
});

test('a history the host cannot give is reported instead of the table', async () => {
  answer = async () => new Response('', { status: 500 });
  renderHistory();

  expect(await screen.findByRole('alert')).toHaveTextContent(texts.de.loadFailed);
  expect(screen.queryByRole('table')).not.toBeInTheDocument();
  expect(screen.queryByText(texts.de.empty)).not.toBeInTheDocument();
});

test("the pager's next page asks the host for page 2, and the focus stays on it", async () => {
  const user = userEvent.setup();
  total = 45;
  renderHistory();
  await screen.findByText(markup.reason);

  const next = screen.getByRole('button', { name: t('dataTable.next') });
  await user.click(next);

  expect(await screen.findByText(onPageTwo.reason)).toBeVisible();
  expect(asked().map((request) => request.query)).toEqual([
    { page: '1', pageSize: '20' },
    { page: '2', pageSize: '20' },
  ]);
  expect(next).toHaveFocus();
});

test('another version starts again at its first page', async () => {
  const user = userEvent.setup();
  total = 45;
  const { rerender } = renderHistory();
  await screen.findByText(markup.reason);
  await user.click(screen.getByRole('button', { name: t('dataTable.next') }));
  await screen.findByText(onPageTwo.reason);

  await rerender({ versionId: otherVersionId });

  expect(await screen.findByText(texts.de.empty)).toBeVisible();
  expect(asked().at(-1)).toEqual({
    path: `/api/editorial/course-versions/${otherVersionId}/returns`,
    query: { page: '1', pageSize: '20' },
  });
});

test('showFirstPage() takes the history back to its first page', async () => {
  const user = userEvent.setup();
  total = 45;
  const history = ref<{ showFirstPage: () => void } | null>(null);
  const Host = defineComponent({
    setup: () => () => h(ReturnHistory, { versionId, ref: history }),
  });
  render(Host, { global: { plugins: [queryPlugin()] } });
  await screen.findByText(markup.reason);
  await user.click(screen.getByRole('button', { name: t('dataTable.next') }));
  await screen.findByText(onPageTwo.reason);

  history.value?.showFirstPage();

  expect(await screen.findByText(markup.reason)).toBeVisible();
  expect(screen.queryByText(onPageTwo.reason)).not.toBeInTheDocument();
});

test('switched to English, the history, its columns and its dates speak English', async () => {
  renderHistory();
  await screen.findByText(markup.reason);

  setLanguage('en-GB', { remember: false });

  const region = await screen.findByRole('region', { name: texts.en.title });
  expect(within(region).getByRole('heading', { level: 2, name: texts.en.title })).toBeInTheDocument();
  expect(within(region).getByRole('columnheader', { name: texts.en.returnedAt })).toBeInTheDocument();
  expect(within(region).getByRole('columnheader', { name: texts.en.reason })).toBeInTheDocument();
  await waitFor(() => expect(within(region).getByText(dateTime(markup.returnedAt))).toBeVisible());
  // The English date is not the German one (19/11/2026 against 19.11.2026): the column follows the language.
  expect(dateTime(markup.returnedAt)).not.toBe(i18n.global.d(new Date(markup.returnedAt), 'dateTime', 'de-DE'));
});

test('in English, a version never returned and a failure say so in English', async () => {
  setLanguage('en-GB', { remember: false });
  records = [];
  renderHistory();

  expect(await screen.findByText(texts.en.empty)).toBeVisible();
  cleanup();

  answer = async () => new Response('', { status: 500 });
  renderHistory();

  expect(await screen.findByRole('alert')).toHaveTextContent(texts.en.loadFailed);
});
