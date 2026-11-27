import { cleanup, screen, waitFor, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import type { CatalogCourseDto } from '@/api/generated';
import { moduleRoutes } from '@/modules';
import { setLanguage, t } from '@/i18n';
import { useSelectedPharmacyStore } from '@/stores/selectedPharmacy';
import {
  anna,
  catalogPath,
  chooseButton,
  dom,
  type Fetch,
  hostFrom,
  hostState,
  type HostState,
  hygiene,
  labelling,
  markt,
  membersOnly,
  membersPath,
  memberCheckbox,
  nextButton,
  now,
  readsOf,
  renderAssignPage,
  stepHeading,
  toLearners,
} from './assignCourseFixtures';
import AssignCoursePage from './AssignCoursePage.vue';

// The assignment wizard (#37, T1): its route, its shell (the heading, the step list, a capability the pharmacy lacks)
// and its first step, the course. The learners, the due date, the review and the result have files of their own.

const notify = vi.hoisted(() => ({ success: vi.fn(), failure: vi.fn() }));
vi.mock('@/lib/notify', () => ({ notify }));

const host = vi.fn<Fetch>();
vi.stubGlobal('fetch', host);

let state: HostState;

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(now);
  localStorage.clear();
  state = hostState();
  host.mockImplementation(hostFrom(state));
});
afterEach(() => {
  vi.useRealTimers();
  host.mockReset();
  localStorage.clear();
});
// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(cleanup);

const stepList = () => screen.getByRole('list', { name: t('compliance.assign.steps.label') });
const steps = ['course', 'learners', 'due', 'review'] as const;

test('the page is at /compliance/assign, for managers, and named in the catalogs', () => {
  const compliance = moduleRoutes.find((route) => route.path === 'compliance');

  expect(compliance?.pages).toContainEqual({
    path: 'assign',
    titleKey: 'modules.compliance.pages.assign.title',
    audience: 'manager',
    component: AssignCoursePage,
  });
  expect(t('modules.compliance.pages.assign.title')).toBe('Schulung zuweisen');
  setLanguage('en-GB', { remember: false });
  expect(t('modules.compliance.pages.assign.title')).toBe('Assign a course');
});

test('the page is headed by its title and lists its four steps, the course step current', async () => {
  await renderAssignPage();

  const heading = screen.getByRole('heading', { level: 1, name: t('modules.compliance.pages.assign.title') });
  expect(heading).toHaveClass('text-title');
  await stepHeading('compliance.assign.course.title');
  const items = within(stepList()).getAllByRole('listitem');
  expect(items).toHaveLength(steps.length);
  steps.forEach((step, index) => expect(items[index]).toHaveTextContent(t(`compliance.assign.steps.${step}`)));
  expect(items[0]).toHaveAttribute('aria-current', 'step');
  for (const item of items.slice(1)) {
    expect(item).not.toHaveAttribute('aria-current');
  }
});

test('one step shows at a time: the course step shows neither the learners nor the due date', async () => {
  await renderAssignPage();

  await stepHeading('compliance.assign.course.title');
  expect(screen.queryByRole('heading', { name: t('compliance.assign.learners.title') })).not.toBeInTheDocument();
  expect(screen.queryByRole('heading', { name: t('compliance.assign.due.title') })).not.toBeInTheDocument();
  expect(screen.queryByRole('heading', { name: t('compliance.assign.review.title') })).not.toBeInTheDocument();
});

// The backend decides (ADR 0003): a manager who may manage members but not assign courses passes the shell's gate, and
// the page tells them they may not assign here instead of offering a form the host would refuse.
test('a manager who may not assign courses in the pharmacy is told so, and shown no form', async () => {
  state.memberships = [membersOnly];

  await renderAssignPage();

  expect(await screen.findByText(t('compliance.assign.notAllowed'))).toBeInTheDocument();
  expect(
    screen.getByRole('heading', { level: 1, name: t('modules.compliance.pages.assign.title') }),
  ).toBeInTheDocument();
  expect(screen.queryByRole('list', { name: t('compliance.assign.steps.label') })).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: t('compliance.assign.next') })).not.toBeInTheDocument();
  expect(readsOf(host, catalogPath)).toEqual([]);
});

test('the course step lists the catalog by title, version and validity, a page of 20', async () => {
  await renderAssignPage();

  const choose = await chooseButton(hygiene);
  expect(screen.getByRole('columnheader', { name: t('compliance.assign.course.columns.title') })).toBeInTheDocument();
  expect(screen.getByRole('columnheader', { name: t('compliance.assign.course.columns.version') })).toBeInTheDocument();
  expect(
    screen.getByRole('columnheader', { name: t('compliance.assign.course.columns.validity') }),
  ).toBeInTheDocument();
  const row = within(choose.closest('tr') ?? document.body);
  expect(row.getByRole('cell', { name: hygiene.title })).toBeInTheDocument();
  expect(row.getByRole('cell', { name: '2' })).toBeInTheDocument();
  expect(row.getByRole('cell', { name: t('compliance.assign.course.months', { count: 12 }, 12) })).toBeInTheDocument();
  expect(await chooseButton(labelling)).toBeInTheDocument();
  expect(readsOf(host, catalogPath)).toEqual([{ page: '1', pageSize: '20' }]);
});

test('the course step says which courses of how many it shows, and pages through a longer catalog', async () => {
  const user = userEvent.setup();
  state.courses = Array.from({ length: 23 }, (_, index) => ({
    ...hygiene,
    courseId: `0198c1a0-0000-7000-8000-0000000001${String(index).padStart(2, '0')}`,
    title: `Schulung ${String(index + 1).padStart(2, '0')}`,
  }));
  await renderAssignPage();

  expect(await screen.findByText(t('dataTable.showing', { first: '1', last: '20', total: '23' }))).toBeInTheDocument();
  await user.click(screen.getByRole('button', { name: t('dataTable.next') }));

  expect(await screen.findByRole('cell', { name: 'Schulung 23' })).toBeInTheDocument();
  expect(readsOf(host, catalogPath)).toContainEqual({ page: '2', pageSize: '20' });
});

test('choosing a course presses its button and no other', async () => {
  const user = userEvent.setup();
  await renderAssignPage();

  const choose = await chooseButton(hygiene);
  expect(choose).toHaveAttribute('aria-pressed', 'false');
  await user.click(choose);

  expect(await chooseButton(hygiene)).toHaveAttribute('aria-pressed', 'true');
  expect(await chooseButton(labelling)).toHaveAttribute('aria-pressed', 'false');

  await user.click(await chooseButton(labelling));
  expect(await chooseButton(labelling)).toHaveAttribute('aria-pressed', 'true');
  expect(await chooseButton(hygiene)).toHaveAttribute('aria-pressed', 'false');
});

// #477: the catalog is read again whenever the step shows, and a course published meanwhile can come before the others.
test("a course's Choose button stays that course's when the catalog is read again with a course before it", async () => {
  const user = userEvent.setup();
  const { queryClient } = await renderAssignPage();
  const choose = await chooseButton(labelling);

  const newer: CatalogCourseDto = {
    ...hygiene,
    courseId: '0198c1a0-0000-7000-8000-0000000000c3',
    courseVersionId: '0198c1a0-0000-7000-8000-0000000000d3',
    title: 'Arzneimittelsicherheit',
  };
  state.courses = [newer, hygiene, labelling];
  await queryClient.invalidateQueries();
  await chooseButton(newer);

  expect(choose).toHaveAccessibleName(t('compliance.assign.course.choose', { title: labelling.title }));
  await user.click(choose);
  expect(await chooseButton(labelling)).toHaveAttribute('aria-pressed', 'true');
  expect(await chooseButton(newer)).toHaveAttribute('aria-pressed', 'false');
});

test('Next without a course is refused with an alert that takes the focus, and stays on the course step', async () => {
  const user = userEvent.setup();
  await renderAssignPage();
  await chooseButton(hygiene);

  await user.click(nextButton());

  const alert = await screen.findByRole('alert');
  expect(alert).toHaveTextContent(t('compliance.assign.course.required'));
  await waitFor(() => expect(alert).toHaveFocus());
  expect(screen.getByRole('heading', { level: 2, name: t('compliance.assign.course.title') })).toBeInTheDocument();
  expect(readsOf(host, membersPath())).toEqual([]);
});

test('Next with a course goes on to the learners, which become the current step', async () => {
  const user = userEvent.setup();
  await renderAssignPage();

  await toLearners(user);

  const items = within(stepList()).getAllByRole('listitem');
  expect(items[1]).toHaveAttribute('aria-current', 'step');
  expect(items[0]).not.toHaveAttribute('aria-current');
  expect(screen.queryByRole('heading', { name: t('compliance.assign.course.title') })).not.toBeInTheDocument();
});

test("entering a step puts the focus on the step's heading", async () => {
  const user = userEvent.setup();
  await renderAssignPage();

  await toLearners(user);

  const heading = await stepHeading('compliance.assign.learners.title');
  expect(heading).toHaveAttribute('tabindex', '-1');
  await waitFor(() => expect(heading).toHaveFocus());
});

test('Back from the learners returns to the course step with the course still chosen', async () => {
  const user = userEvent.setup();
  await renderAssignPage();
  await toLearners(user);

  await user.click(screen.getByRole('button', { name: t('compliance.assign.back') }));

  const heading = await stepHeading('compliance.assign.course.title');
  await waitFor(() => expect(heading).toHaveFocus());
  expect(await chooseButton(hygiene)).toHaveAttribute('aria-pressed', 'true');
});

test('a catalog with no published course says so', async () => {
  state.courses = [];

  await renderAssignPage();

  expect(await screen.findByText(t('compliance.assign.course.empty'))).toBeInTheDocument();
});

test('the course step is busy while the catalog loads', async () => {
  state.catalog = () => new Promise<Response>(() => undefined);

  await renderAssignPage();

  await waitFor(() => expect(screen.getByRole('table')).toHaveAttribute('aria-busy', 'true'));
});

test('a catalog that cannot be read says so in an alert, and Retry reads it again', async () => {
  const user = userEvent.setup();
  state.catalog = async () => new Response('', { status: 500 });
  await renderAssignPage();

  const alert = await screen.findByRole('alert');
  expect(alert).toHaveTextContent(t('compliance.assign.course.loadFailed'));
  state.catalog = undefined;
  await user.click(within(alert).getByRole('button', { name: t('compliance.assign.retry') }));

  expect(await chooseButton(hygiene)).toBeInTheDocument();
  expect(readsOf(host, catalogPath)).toHaveLength(2);
});

// The state lives in the page and belongs to one pharmacy: switching the pharmacy starts the wizard afresh.
test('switching the pharmacy starts the wizard afresh, on the course step with no course chosen', async () => {
  const user = userEvent.setup();
  state.memberships = [markt, dom];
  localStorage.setItem('pharmacy', markt.id);
  const { pinia } = await renderAssignPage();
  await toLearners(user);
  await user.click(await memberCheckbox(anna));

  useSelectedPharmacyStore(pinia).choose(dom.id);

  const heading = await stepHeading('compliance.assign.course.title');
  await waitFor(() => expect(heading).toBeInTheDocument());
  expect(await chooseButton(hygiene)).toHaveAttribute('aria-pressed', 'false');
  await user.click(await chooseButton(hygiene));
  await user.click(nextButton());
  await stepHeading('compliance.assign.learners.title');
  await waitFor(() => expect(readsOf(host, membersPath(dom))).not.toEqual([]));
});
