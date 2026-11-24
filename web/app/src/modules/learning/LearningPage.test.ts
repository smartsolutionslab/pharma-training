import { cleanup, screen, waitFor, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import { setLanguage, t } from '@/i18n';
import {
  answering,
  assignmentsOf,
  assignmentsPath,
  type Fetch,
  inProgress,
  locked,
  markt,
  membershipsAnswer,
  minePath,
  myCourses,
  type MyCoursesHost,
  myCoursesHost,
  never,
  open,
  overdue,
  problem,
  readOf,
  readsOf,
  renderMyCoursesPage,
  retired,
} from './myCoursesFixtures';

// My courses (#38, T2) replaces the Learning module's sample at /learning: the employee's own assigned courses in the
// current pharmacy. Here its shell (the heading, whose courses they are), what it asks the host, the order it shows them
// in, how it waits, and what it says when the host refuses it or fails. Each item, the empty list and the pager have
// files of their own.

const fetch = vi.fn<Fetch>();
vi.stubGlobal('fetch', fetch);

let host: MyCoursesHost;

beforeEach(() => {
  localStorage.clear();
  host = myCoursesHost();
  fetch.mockImplementation(answering(host));
});
afterEach(() => {
  fetch.mockReset();
  localStorage.clear();
});
// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(cleanup);

const intro = () => t('learning.myCourses.intro', { pharmacy: markt.name });
const listHeading = () => screen.queryByRole('heading', { level: 2, name: t('learning.myCourses.list.title') });

/** The titles of the listed courses, in the order the list shows them: each item's h3. */
async function listedTitles(): Promise<string[]> {
  const list = await screen.findByRole('list');
  return within(list)
    .getAllByRole('listitem')
    .map((item) => within(item).getByRole('heading', { level: 3 }).textContent?.trim() ?? '');
}

/** The page's one alert, once it says this text: the text may share the alert with a button. */
async function alertSaying(text: string): Promise<HTMLElement> {
  const shown = await screen.findByText(text);
  const alert = shown.closest<HTMLElement>('[role="alert"]');
  expect(alert, `${text} is in an alert`).not.toBeNull();
  expect(screen.getAllByRole('alert')).toHaveLength(1);
  return alert ?? shown;
}

test('the page is headed Meine Schulungen and says whose courses it lists', async () => {
  await renderMyCoursesPage();

  const main = within(screen.getByRole('main'));
  const heading = main.getByRole('heading', { level: 1, name: 'Meine Schulungen' });
  expect(heading).toHaveClass('text-title');
  expect(intro()).toBe('Ihre Schulungen in Apotheke am Markt: Überfällige zuerst, dann nach Fälligkeit.');
  expect(await main.findByText(intro())).toBeInTheDocument();
});

test('the page heads its list Zugewiesene Schulungen', async () => {
  await renderMyCoursesPage();

  const heading = await screen.findByRole('heading', { level: 2, name: 'Zugewiesene Schulungen' });
  const section = heading.closest('section');
  expect(section).not.toBeNull();
  expect(section).toHaveAttribute('aria-labelledby', heading.id);
  expect(within(section ?? heading).getByRole('list')).toBeInTheDocument();
});

test('the page shows the sample no more', async () => {
  await renderMyCoursesPage();

  await screen.findByText(intro());
  expect(screen.queryByRole('heading', { name: t('samples.title') })).not.toBeInTheDocument();
  expect(screen.queryByRole('heading', { name: t('samples.manage') })).not.toBeInTheDocument();
  expect(readsOf(fetch, '/api/learning/samples')).toEqual([]);
  expect(readsOf(fetch, '/api/learning/permissions/manage')).toEqual([]);
});

test('the page asks for the first page of 50 of the caller’s courses in the current pharmacy', async () => {
  await renderMyCoursesPage();

  await waitFor(() => expect(readsOf(fetch, assignmentsPath)).toEqual([readOf(1)]));
});

test('the page asks nothing about courses while the memberships load, and asks once they are known', async () => {
  let answer: (response: Response) => void = () => undefined;
  host.mine = () =>
    new Promise<Response>((resolve) => {
      answer = resolve;
    });
  await renderMyCoursesPage();

  await waitFor(() => expect(readsOf(fetch, minePath)).toHaveLength(1));
  expect(readsOf(fetch, assignmentsPath)).toEqual([]);

  answer(membershipsAnswer([markt]));

  await waitFor(() => expect(readsOf(fetch, assignmentsPath)).toEqual([readOf(1)]));
});

// The host orders overdue first, then by due date, then by title, before it pages (#35): the page keeps that order.
test('lists the courses in the order the host answers them', async () => {
  await renderMyCoursesPage();

  expect(await listedTitles()).toEqual(myCourses.map((course) => course.courseTitle));
  expect(await listedTitles()).toEqual([
    'Hygiene in der Apotheke',
    'Datenschutz',
    'Notfallmanagement',
    'Arzneimittelsicherheit',
    'Betäubungsmittel',
  ]);
});

test('sorts nothing itself: an answer in another order is listed in that order', async () => {
  const shuffled = [open, retired, overdue, inProgress, locked];
  host.assignments = async () => Response.json(assignmentsOf(shuffled));
  await renderMyCoursesPage();

  expect(await listedTitles()).toEqual([
    'Arzneimittelsicherheit',
    'Betäubungsmittel',
    'Hygiene in der Apotheke',
    'Notfallmanagement',
    'Datenschutz',
  ]);
});

test('the list keeps its semantics: a list with role list, one item per course', async () => {
  await renderMyCoursesPage();

  const list = await screen.findByRole('list');
  expect(list.tagName).toBe('UL');
  expect(list).toHaveAttribute('role', 'list');
  expect(within(list).getAllByRole('listitem')).toHaveLength(5);
});

test('while the courses load it says so to screen readers and shows three skeleton items, not a list', async () => {
  host.assignments = never;
  await renderMyCoursesPage();

  const main = screen.getByRole('main');
  await waitFor(() => expect(within(main).getByRole('status')).toHaveTextContent('Wird geladen…'));
  expect(within(main).getByRole('status')).toHaveClass('sr-only');
  // Each skeleton item is hidden from screen readers as a whole: the status says the list is loading.
  const skeletonItems = Array.from(main.querySelectorAll<HTMLElement>('[aria-hidden="true"]')).filter(
    (element) =>
      element.querySelector('[data-slot="skeleton"]') !== null &&
      element.parentElement?.closest('[aria-hidden="true"]') === null,
  );
  expect(skeletonItems).toHaveLength(3);
  expect(
    Array.from(main.querySelectorAll('[data-slot="skeleton"]')).every(
      (skeleton) => skeleton.closest('[aria-hidden="true"]') !== null,
    ),
  ).toBe(true);
  expect(screen.queryByRole('list')).not.toBeInTheDocument();
  expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  expect(screen.queryByText(t('learning.myCourses.empty', { pharmacy: markt.name }))).not.toBeInTheDocument();
});

// What the page says for each refusal of the spec's "Refusal → UI": a code it has words for says what it is, never the
// host's words; the alert takes the place of the list, and the heading and the intro stay.
test.each([
  [404, 'compliance.pharmacy-not-found', 'learning.myCourses.pharmacyNotFound'],
  [403, 'compliance.identity-incomplete', 'learning.myCourses.identityIncomplete'],
])('courses the host refuses with %i %s say %s, in place of the list', async (status, code, key) => {
  host.assignments = async () => problem(status, code);
  await renderMyCoursesPage();

  const alert = await alertSaying(t(key));
  expect(alert).not.toHaveTextContent('Words of the host');
  expect(alert.tagName).toBe('P');
  expect(screen.getByRole('heading', { level: 1, name: 'Meine Schulungen' })).toBeInTheDocument();
  expect(screen.getByText(intro())).toBeInTheDocument();
  expect(screen.queryByRole('list')).not.toBeInTheDocument();
  expect(listHeading()).not.toBeInTheDocument();
  expect(within(alert).queryByRole('button')).not.toBeInTheDocument();
});

test('a caller who is no longer a member is told so in German', async () => {
  host.assignments = async () => problem(404, 'compliance.pharmacy-not-found');
  await renderMyCoursesPage();

  expect(await alertSaying('Sie sind nicht mehr Mitglied dieser Apotheke.')).toBeVisible();
});

// The membership has ended: the memberships are read again, so the shell lets go of it.
test('a pharmacy the caller is no longer a member of reads the memberships again', async () => {
  host.assignments = async () => problem(404, 'compliance.pharmacy-not-found');
  await renderMyCoursesPage();

  await alertSaying(t('learning.myCourses.pharmacyNotFound'));
  await waitFor(() => expect(readsOf(fetch, minePath).length).toBeGreaterThan(1));
});

// A module code the catalogs do not know shows the host's own words (#147, ADR 0007); reading again may help.
test.each([
  ['Compliance', () => problem(400, 'compliance.not-in-the-catalogs', 'Es wurde keine Apotheke angegeben.')],
  ['another module', () => problem(403, 'organisation.x', 'Es wurde keine Apotheke angegeben.')],
])('a refusal of %s the catalogs do not know shows the host’s own words, and Retry', async (_name, refusal) => {
  host.assignments = async () => refusal();
  await renderMyCoursesPage();

  const alert = await alertSaying('Es wurde keine Apotheke angegeben.');
  expect(screen.queryByRole('list')).not.toBeInTheDocument();
  expect(within(alert).getByRole('button', { name: 'Erneut versuchen' })).toBeInTheDocument();
});

test.each([
  ['a failure of the host', () => Promise.resolve(new Response('', { status: 500 }))],
  ['a bad request without a code', () => Promise.resolve(problem(400))],
  ['the network', () => Promise.reject(new TypeError('Failed to fetch'))],
])('%s says the courses could not be loaded, and Retry reads them again', async (_name, failure) => {
  const user = userEvent.setup();
  host.assignments = failure;
  await renderMyCoursesPage();

  const alert = await alertSaying('Ihre Schulungen konnten nicht geladen werden.');
  expect(alert).not.toHaveTextContent('Words of the host');
  expect(screen.queryByRole('list')).not.toBeInTheDocument();
  host.assignments = async () => Response.json(assignmentsOf());
  await user.click(within(alert).getByRole('button', { name: 'Erneut versuchen' }));

  await waitFor(() => expect(readsOf(fetch, assignmentsPath)).toHaveLength(2));
  await waitFor(() => expect(screen.queryByRole('alert')).not.toBeInTheDocument());
  expect(await listedTitles()).toHaveLength(5);
});

test('a refusal does not move the focus', async () => {
  host.assignments = async () => problem(404, 'compliance.pharmacy-not-found');
  await renderMyCoursesPage();

  await alertSaying(t('learning.myCourses.pharmacyNotFound'));
  expect(document.activeElement).toBe(document.body);
});

test('a refusal is shown in English once switched', async () => {
  setLanguage('en-GB', { remember: false });
  host.assignments = async () => problem(403, 'compliance.identity-incomplete');
  await renderMyCoursesPage();

  expect(await alertSaying('Your account gives no name or valid email address. Complete your profile.')).toBeVisible();
  expect(screen.getByRole('heading', { level: 1, name: 'My courses' })).toBeInTheDocument();
  expect(screen.getByText('Your courses in Apotheke am Markt: overdue first, then by due date.')).toBeInTheDocument();
});
