import { cleanup, screen, waitFor, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import { setLanguage, t } from '@/i18n';
import { moduleRoutes } from '@/modules';
import CompliancePage from './CompliancePage.vue';
import {
  type Fetch,
  answering,
  markt,
  matrixHost,
  type MatrixHost,
  matrixOf,
  matrixPath,
  membersOnly,
  minePath,
  never,
  now,
  problem,
  readsOf,
  renderMatrixPage,
} from './matrixFixtures';

// The team matrix (#36, T2) replaces the module's sample at /compliance: the manager's main page, for managers. Here its
// route, its shell (the heading, whose team it is, the way to assign a course), what it asks the host, how it waits,
// and what it says when the host refuses it or fails. The table, the rate, the filters and the person have files of
// their own.

const fetch = vi.fn<Fetch>();
vi.stubGlobal('fetch', fetch);

let host: MatrixHost;

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(now);
  localStorage.clear();
  host = matrixHost();
  fetch.mockImplementation(answering(host));
});
afterEach(() => {
  vi.useRealTimers();
  fetch.mockReset();
  localStorage.clear();
});
// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(cleanup);

const assignLink = () => screen.queryByRole('link', { name: t('modules.compliance.pages.assign.title') });
const intro = () => t('compliance.matrix.intro', { pharmacy: markt.name });
const region = () => screen.getByRole('region', { name: t('compliance.matrix.table.label') });

/** The page's one alert, once it says this text: the text may share the alert with a button. */
async function alertSaying(text: string): Promise<HTMLElement> {
  const shown = await screen.findByText(text);
  const alert = shown.closest<HTMLElement>('[role="alert"]');
  expect(alert, `${text} is in an alert`).not.toBeNull();
  expect(screen.getAllByRole('alert')).toHaveLength(1);
  return alert ?? shown;
}

test('the matrix is the module’s page at /compliance, for managers, named Team-Matrix in both languages', () => {
  const compliance = moduleRoutes.find((route) => route.path === 'compliance');

  expect(compliance).toMatchObject({
    path: 'compliance',
    titleKey: 'modules.compliance.title',
    audience: 'manager',
    component: CompliancePage,
  });
  expect(t('modules.compliance.title')).toBe('Team-Matrix');
  setLanguage('en-GB', { remember: false });
  expect(t('modules.compliance.title')).toBe('Team matrix');
});

test('the page is headed Team-Matrix and says whose team it shows', async () => {
  await renderMatrixPage();

  const main = within(screen.getByRole('main'));
  const heading = main.getByRole('heading', { level: 1, name: 'Team-Matrix' });
  expect(heading).toHaveClass('text-title');
  expect(await main.findByText(intro())).toBeInTheDocument();
  expect(intro()).toBe('Wer in Apotheke am Markt welche Schulung bis wann abschließen muss.');
});

test('the page shows the sample no more', async () => {
  await renderMatrixPage();

  await screen.findByText(intro());
  expect(screen.queryByRole('heading', { name: t('samples.title') })).not.toBeInTheDocument();
  expect(screen.queryByRole('heading', { name: t('samples.manage') })).not.toBeInTheDocument();
  expect(readsOf(fetch, '/api/compliance/samples')).toEqual([]);
  expect(readsOf(fetch, '/api/compliance/permissions/manage')).toEqual([]);
});

test('a manager who may assign courses is offered the way to assign one', async () => {
  await renderMatrixPage();

  await screen.findByText(intro());
  await waitFor(() => expect(assignLink()).toHaveAttribute('href', '/compliance/assign'));
});

// Hiding only (ADR 0003): the wizard asks the host itself.
test('a manager who may not assign courses is not offered the way to assign one, but sees the matrix', async () => {
  host.memberships = [membersOnly];
  await renderMatrixPage();

  await screen.findByText(intro());
  await waitFor(() => expect(readsOf(fetch, matrixPath())).not.toEqual([]));
  expect(assignLink()).not.toBeInTheDocument();
});

test('the page asks for the first page of 50 of the current pharmacy’s matrix, with no job function', async () => {
  await renderMatrixPage();

  await waitFor(() => expect(readsOf(fetch, matrixPath())).toEqual([{ page: '1', pageSize: '50' }]));
});

test('while the matrix loads the region is busy and shows skeletons', async () => {
  host.matrix = never;
  await renderMatrixPage();

  await waitFor(() => expect(region()).toHaveAttribute('aria-busy', 'true'));
  expect(screen.getByRole('main').querySelector('[data-slot="skeleton"]')).not.toBeNull();
  expect(screen.queryByRole('alert')).not.toBeInTheDocument();
});

// What the page says for each refusal of the spec's "Refusal → UI": a code it has words for says what it is, never the
// host's words; the alert takes the place of the rate, the filters and the table, and the heading and the intro stay.
test.each([
  [403, 'compliance.capability-missing', 'compliance.matrix.notAllowed'],
  [404, 'compliance.pharmacy-not-found', 'errors.compliance.pharmacy-not-found'],
  [403, 'compliance.identity-incomplete', 'errors.compliance.identity-incomplete'],
])('a matrix the host refuses with %i %s says %s, in place of the matrix', async (status, code, key) => {
  host.matrix = async () => problem(status, code);
  await renderMatrixPage();

  const alert = await alertSaying(t(key));
  expect(alert).not.toHaveTextContent('Words of the host');
  expect(alert.tagName).toBe('P');
  expect(screen.getByRole('heading', { level: 1, name: 'Team-Matrix' })).toBeInTheDocument();
  expect(screen.getByText(intro())).toBeInTheDocument();
  expect(screen.queryByRole('region', { name: t('compliance.matrix.table.label') })).not.toBeInTheDocument();
  expect(screen.queryByRole('table')).not.toBeInTheDocument();
});

test('a member without the capability is told in German they may not view the team’s training status', async () => {
  host.matrix = async () => problem(403, 'compliance.capability-missing');
  await renderMatrixPage();

  expect(await alertSaying('Sie dürfen den Schulungsstand des Teams in dieser Apotheke nicht sehen.')).toBeVisible();
});

// The membership has ended: the memberships are read again, so the shell lets go of it.
test('a pharmacy the caller is no longer a member of reads the memberships again', async () => {
  host.matrix = async () => problem(404, 'compliance.pharmacy-not-found');
  await renderMatrixPage();

  await alertSaying(t('errors.compliance.pharmacy-not-found'));
  await waitFor(() => expect(readsOf(fetch, minePath).length).toBeGreaterThan(1));
});

// A code the page has no words of its own for, but the catalog has (#147): its text, never the host's.
test.each([
  ['de-DE', 'Ihre eigene Zuweisung kann nur ein anderer Apothekenleiter ändern oder zurückziehen.'],
  ['en-GB', 'Only another pharmacy manager can change or withdraw your own assignment.'],
] as const)('a matrix refused with own-assignment says the catalog text, in %s', async (language, text) => {
  setLanguage(language, { remember: false });
  host.matrix = async () => problem(403, 'compliance.own-assignment', 'SERVER WORDS');
  await renderMatrixPage();

  const alert = await alertSaying(text);
  expect(alert).not.toHaveTextContent('SERVER WORDS');
});

test('another refusal of the module, whose code the catalog does not know, shows the host’s own words', async () => {
  host.matrix = async () => problem(409, 'compliance.something-new', 'Etwas Neues aus dem Host.');
  await renderMatrixPage();

  await alertSaying('Etwas Neues aus dem Host.');
  expect(screen.queryByRole('table')).not.toBeInTheDocument();
});

test.each([
  ['a failure of the host', () => new Response('', { status: 500 })],
  ['a bad request without a code', () => problem(400)],
])('%s says the matrix could not be loaded, and Retry reads it again', async (_name, failure) => {
  const user = userEvent.setup();
  host.matrix = async () => failure();
  await renderMatrixPage();

  const alert = await alertSaying(t('compliance.matrix.loadFailed'));
  expect(alert).not.toHaveTextContent('Words of the host');
  host.matrix = async () => Response.json(matrixOf());
  await user.click(within(alert).getByRole('button', { name: t('compliance.matrix.retry') }));

  await waitFor(() => expect(readsOf(fetch, matrixPath())).toHaveLength(2));
  await waitFor(() => expect(screen.queryByRole('alert')).not.toBeInTheDocument());
});

// Retry shows for any failure to load the matrix but the two a second read cannot turn round: the member may not see it,
// or is no member any more. A refusal keeps its own text beside Retry.
test.each([
  ['a framework’s invalid-argument', () => problem(400, 'invalid-argument'), () => t('errors.invalid-argument')],
  [
    'another module’s code',
    () => problem(403, 'organisation.capability-missing'),
    () => t('errors.organisation.capability-missing'),
  ],
  [
    'a code of the module’s the catalog knows',
    () => problem(403, 'compliance.identity-incomplete'),
    () => t('errors.compliance.identity-incomplete'),
  ],
  [
    'a code of the module’s the catalog does not know',
    () => problem(409, 'compliance.something-new', 'Etwas Neues aus dem Host.'),
    () => 'Etwas Neues aus dem Host.',
  ],
])('%s keeps its text, and Retry reads the matrix again', async (_name, failure, text) => {
  const user = userEvent.setup();
  host.matrix = async () => failure();
  await renderMatrixPage();

  const alert = await alertSaying(text());
  host.matrix = async () => Response.json(matrixOf());
  await user.click(within(alert).getByRole('button', { name: t('compliance.matrix.retry') }));

  await waitFor(() => expect(readsOf(fetch, matrixPath())).toHaveLength(2));
  await waitFor(() => expect(screen.queryByRole('alert')).not.toBeInTheDocument());
});

test.each([
  [403, 'compliance.capability-missing', 'compliance.matrix.notAllowed'],
  [404, 'compliance.pharmacy-not-found', 'errors.compliance.pharmacy-not-found'],
])('a %i %s offers no Retry: reading again cannot help', async (status, code, key) => {
  host.matrix = async () => problem(status, code);
  await renderMatrixPage();

  const alert = await alertSaying(t(key));
  expect(within(alert).queryByRole('button', { name: t('compliance.matrix.retry') })).not.toBeInTheDocument();
});

test('a refusal is shown in English once switched', async () => {
  setLanguage('en-GB', { remember: false });
  host.matrix = async () => problem(403, 'compliance.capability-missing');
  await renderMatrixPage();

  expect(await alertSaying("You may not view the team's training status in this pharmacy.")).toBeVisible();
  expect(screen.getByRole('heading', { level: 1, name: 'Team matrix' })).toBeInTheDocument();
});
