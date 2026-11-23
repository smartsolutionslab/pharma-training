import { QueryClient, VueQueryPlugin, type VueQueryPluginOptions } from '@tanstack/vue-query';
import { cleanup, render, screen, waitFor, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { type IdTokenClaims, User } from 'oidc-client-ts';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import { defineComponent, h, ref } from 'vue';
import type { EditorialCourseVersionReturnDto } from '@/api/generated';
import { type AuthClient, authKey, createAuth } from '@/auth/useAuth';
import { i18n, setLanguage, t } from '@/i18n';
import ReturnHistory from './ReturnHistory.vue';

// The host, as GET /editorial/course-versions/{id}/returns reaches it through the generated client: each page answers
// the records the test gives it, in the host's order (newest first), with the total the test says.
const host = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>();
vi.stubGlobal('fetch', host);

const versionId = '0198c1a0-0000-7000-8000-0000000000c1';
const otherVersionId = '0198c1a0-0000-7000-8000-0000000000c2';
// The signed-in editor's subject (the ID token's sub) and another editor's: returnedBy is the sub of whoever returned it.
const editor = '0198c1a0-0000-7000-8000-0000000000e1';
const colleague = '0198c1a0-0000-7000-8000-0000000000e2';

const twoLines: EditorialCourseVersionReturnDto = {
  id: '0198c1a0-0000-7000-8000-0000000000a2',
  returnedBy: editor,
  returnedAt: '2026-11-21T14:05:00Z',
  reason: 'Kapitel 2 braucht eine Quelle.\nDie Tabelle in Kapitel 3 ist veraltet.',
};
const markup: EditorialCourseVersionReturnDto = {
  id: '0198c1a0-0000-7000-8000-0000000000a1',
  returnedBy: colleague,
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
    returnedBy: 'Zurückgegeben von',
    reason: 'Grund',
    // Each reads as the continuation of "Zurückgegeben von …", hence the dative.
    byYou: 'Ihnen',
    byAnotherEditor: 'einer anderen Person aus der Redaktion',
    empty: 'Diese Version wurde noch nie zur Überarbeitung zurückgegeben.',
    loadFailed: 'Die Rückgaben dieser Version konnten nicht geladen werden.',
  },
  en: {
    title: 'Returns',
    returnedAt: 'Returned on',
    returnedBy: 'Returned by',
    reason: 'Reason',
    // Each stands alone in its cell, so it starts with a capital.
    byYou: 'You',
    byAnotherEditor: 'Another person in the editorial team',
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

/** The ID token claims of a signed-in user: the editor above unless the test says otherwise. */
const claims = (overrides: Partial<IdTokenClaims> = {}): IdTokenClaims => ({
  sub: editor,
  iss: 'http://localhost:8080/realms/pharma-training',
  aud: 'pharma-training-web',
  exp: 0,
  iat: 0,
  preferred_username: 'developer',
  ...overrides,
});

const signedIn = (profile: IdTokenClaims) =>
  new User({
    access_token: 'access-token',
    token_type: 'Bearer',
    profile,
    expires_at: Math.floor(Date.now() / 1000) + 300,
  });

/** oidc-client-ts's UserManager with the stored user given (or none), as SignedIn.test.ts fakes it. */
function client(user: User | null): AuthClient {
  return {
    getUser: vi.fn<AuthClient['getUser']>(() => Promise.resolve(user)),
    signinCallback: vi.fn<AuthClient['signinCallback']>(() => Promise.resolve(user ?? undefined)),
    signinRedirect: vi.fn<AuthClient['signinRedirect']>(() => Promise.resolve()),
    signoutRedirect: vi.fn<AuthClient['signoutRedirect']>(() => Promise.resolve()),
    events: {
      addUserLoaded: vi.fn(),
      addUserUnloaded: vi.fn(),
      addAccessTokenExpired: vi.fn(),
      addSilentRenewError: vi.fn(),
    },
  };
}

/** The user useAuth() answers with for the next render: the editor, unless a test signs in someone else or no one. */
let user: User | null;
beforeEach(() => {
  user = signedIn(claims());
});

const authProvided = () => ({ [authKey]: createAuth(client(user)) });

function renderHistory(id = versionId) {
  return render(ReturnHistory, {
    props: { versionId: id },
    global: { plugins: [queryPlugin()], provide: authProvided() },
  });
}

/** The history's table and its data rows, the header row left out. */
const table = (title = texts.de.title) => within(screen.getByRole('region', { name: title })).getByRole('table');
const rows = (title = texts.de.title) => within(table(title)).getAllByRole('row').slice(1);
const cells = (row: HTMLElement) => within(row).getAllByRole('cell');

const dateTime = (instant: string) => i18n.global.d(new Date(instant), 'dateTime');

/** Each data row's "returned by" cell, the second of its three. */
const returnedBy = (title = texts.de.title) => rows(title).map((row) => cells(row)[1]?.textContent.trim());

/**
 * The catalogs as plain data: a loose view, since editorial.returns.columns.returnedBy, editorial.returns.byYou and
 * editorial.returns.byAnotherEditor are not in MessageSchema yet.
 */
type LooseReturns = { columns?: Record<string, unknown> } & Record<string, unknown>;
const catalog = (language: 'de-DE' | 'en-GB'): LooseReturns => {
  const messages: { editorial?: { returns?: LooseReturns } } = i18n.global.getLocaleMessage(language);
  return messages.editorial?.returns ?? {};
};

test('the history is a region headed "Rückgaben", with the date, who returned it and the reason as its columns', async () => {
  renderHistory();

  const region = screen.getByRole('region', { name: texts.de.title });
  expect(within(region).getByRole('heading', { level: 2, name: texts.de.title })).toBeInTheDocument();
  expect(await within(region).findByRole('columnheader', { name: texts.de.returnedAt })).toBeInTheDocument();
  expect(within(region).getByRole('columnheader', { name: texts.de.returnedBy })).toBeInTheDocument();
  expect(within(region).getByRole('columnheader', { name: texts.de.reason })).toBeInTheDocument();
});

test('"Zurückgegeben von" stands between the date and the reason', async () => {
  renderHistory();

  await screen.findByText(markup.reason);
  // The table's own header cells (the actions column, if any, has no name and is left out).
  const headers = within(table())
    .getAllByRole('columnheader')
    .map((header) => header.textContent.trim())
    .filter((name) => name !== '');
  expect(headers).toEqual([texts.de.returnedAt, texts.de.returnedBy, texts.de.reason]);
});

test('the catalogs hold the column and both forms, in German and in English', () => {
  expect(catalog('de-DE').columns?.['returnedBy']).toBe(texts.de.returnedBy);
  expect(catalog('de-DE')['byYou']).toBe(texts.de.byYou);
  expect(catalog('de-DE')['byAnotherEditor']).toBe(texts.de.byAnotherEditor);
  expect(catalog('en-GB').columns?.['returnedBy']).toBe(texts.en.returnedBy);
  expect(catalog('en-GB')['byYou']).toBe(texts.en.byYou);
  expect(catalog('en-GB')['byAnotherEditor']).toBe(texts.en.byAnotherEditor);
});

test("it asks the host for the version's first page of returns at the default size", async () => {
  renderHistory();

  await screen.findByText(markup.reason);
  expect(asked()).toEqual([
    { path: `/api/editorial/course-versions/${versionId}/returns`, query: { page: '1', pageSize: '20' } },
  ]);
});

test("the rows are in the host's order, each with its date in the browser's time zone, who returned it and its reason", async () => {
  renderHistory();

  await screen.findByText(markup.reason);
  // The signed-in user's sub is loaded asynchronously: wait until the rows they returned say so.
  await waitFor(() =>
    expect(rows().map((row) => cells(row).map((cell) => cell.textContent.trim()))).toEqual([
      [dateTime(twoLines.returnedAt), texts.de.byYou, twoLines.reason],
      [dateTime(markup.returnedAt), texts.de.byAnotherEditor, markup.reason],
      [dateTime(script.returnedAt), texts.de.byYou, script.reason],
    ]),
  );
});

test('a return whose returnedBy is the signed-in user\'s sub reads "Ihnen", any other "einer anderen Person aus der Redaktion"', async () => {
  renderHistory();

  await screen.findByText(markup.reason);
  await waitFor(() => expect(returnedBy()).toEqual([texts.de.byYou, texts.de.byAnotherEditor, texts.de.byYou]));
});

test("the user is matched by the ID token's sub, not by their name", async () => {
  // A user named like the record's returnedBy, with another sub: the record is still another editor's.
  user = signedIn(claims({ sub: colleague, preferred_username: editor, name: editor }));
  records = [twoLines, markup];
  renderHistory();

  await screen.findByText(markup.reason);
  await waitFor(() => expect(returnedBy()).toEqual([texts.de.byAnotherEditor, texts.de.byYou]));
});

test("with no user signed in, every return reads as another editor's", async () => {
  user = null;
  renderHistory();

  await screen.findByText(markup.reason);
  await waitFor(() =>
    expect(returnedBy()).toEqual([texts.de.byAnotherEditor, texts.de.byAnotherEditor, texts.de.byAnotherEditor]),
  );
  expect(screen.queryByText(texts.de.byYou)).not.toBeInTheDocument();
});

test("with a signed-in user whose ID token has no sub, every return reads as another editor's", async () => {
  // An ID token without the claim: oidc-client-ts types it as always there, so it is taken out of a copy.
  const withoutSub: Partial<IdTokenClaims> = claims();
  delete withoutSub.sub;
  user = signedIn(withoutSub as IdTokenClaims);
  renderHistory();

  await screen.findByText(markup.reason);
  await waitFor(() =>
    expect(returnedBy()).toEqual([texts.de.byAnotherEditor, texts.de.byAnotherEditor, texts.de.byAnotherEditor]),
  );
  expect(screen.queryByText(texts.de.byYou)).not.toBeInTheDocument();
});

test('a reason of two lines keeps both, on two lines', async () => {
  renderHistory();

  await screen.findByText(markup.reason);
  const [first] = rows();
  if (first === undefined) {
    throw new Error('The history has no rows.');
  }
  // The reason is the third column, after the date and who returned it.
  const reason = cells(first)[2];
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

test('coming back to a version shows its first page, not the page it was turned to before', async () => {
  const user = userEvent.setup();
  total = 45;
  const { rerender } = renderHistory();
  await screen.findByText(markup.reason);
  await user.click(screen.getByRole('button', { name: t('dataTable.next') }));
  await screen.findByText(onPageTwo.reason);
  await rerender({ versionId: otherVersionId });
  await screen.findByText(texts.de.empty);

  await rerender({ versionId });

  await waitFor(() =>
    expect(asked().at(-1)).toEqual({
      path: `/api/editorial/course-versions/${versionId}/returns`,
      query: { page: '1', pageSize: '20' },
    }),
  );
  expect(await screen.findByText(markup.reason)).toBeVisible();
  expect(screen.queryByText(onPageTwo.reason)).not.toBeInTheDocument();
});

test('showFirstPage() takes the history back to its first page', async () => {
  const user = userEvent.setup();
  total = 45;
  const history = ref<{ showFirstPage: () => void } | null>(null);
  const Host = defineComponent({
    setup: () => () => h(ReturnHistory, { versionId, ref: history }),
  });
  render(Host, { global: { plugins: [queryPlugin()], provide: authProvided() } });
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
  expect(within(region).getByRole('columnheader', { name: texts.en.returnedBy })).toBeInTheDocument();
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

test('in English, the returns read "You" and "Another person in the editorial team"', async () => {
  setLanguage('en-GB', { remember: false });
  renderHistory();

  await screen.findByText(markup.reason);
  await waitFor(() =>
    expect(returnedBy(texts.en.title)).toEqual([texts.en.byYou, texts.en.byAnotherEditor, texts.en.byYou]),
  );
  const headers = within(table(texts.en.title))
    .getAllByRole('columnheader')
    .map((header) => header.textContent.trim())
    .filter((name) => name !== '');
  expect(headers).toEqual([texts.en.returnedAt, texts.en.returnedBy, texts.en.reason]);
});

test('in English, with no user signed in, every return reads "Another person in the editorial team"', async () => {
  setLanguage('en-GB', { remember: false });
  user = null;
  renderHistory();

  await screen.findByText(markup.reason);
  await waitFor(() =>
    expect(returnedBy(texts.en.title)).toEqual([
      texts.en.byAnotherEditor,
      texts.en.byAnotherEditor,
      texts.en.byAnotherEditor,
    ]),
  );
});
