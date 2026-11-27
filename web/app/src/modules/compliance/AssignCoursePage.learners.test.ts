import { cleanup, screen, waitFor, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import { t } from '@/i18n';
import {
  anna,
  ben,
  carla,
  david,
  type Fetch,
  hostFrom,
  hostState,
  type HostState,
  jobFunctionButton,
  markt,
  memberCheckbox,
  membersPath,
  nextButton,
  now,
  readsOf,
  renderAssignPage,
  selectedCount,
  staff,
  stepHeading,
  team,
  toLearners,
} from './assignCourseFixtures';

// The wizard's second step (#37, T2): the learners, from the pharmacy's active members (GET
// /compliance/pharmacies/{id}/members, #217). One by one, or everyone with a job function across every page; at least
// one and at most 100.

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

/** The live region that counts the people selected. */
async function liveCount(count: number) {
  const region = await screen.findByText(selectedCount(count));
  const polite = region.getAttribute('role') === 'status' || region.getAttribute('aria-live') === 'polite';
  expect(polite, 'the count is a polite live region (role="status" or aria-live="polite")').toBe(true);
  return region;
}

const membersReads = () => readsOf(host, membersPath());

test("the learners step lists the pharmacy's members by name and job function, each with a checkbox", async () => {
  const user = userEvent.setup();
  await renderAssignPage();

  await toLearners(user);

  for (const member of team) {
    const checkbox = await memberCheckbox(member);
    expect(checkbox).not.toBeChecked();
    const row = within(checkbox.closest('tr') ?? document.body);
    expect(row.getByText(member.name)).toBeInTheDocument();
    expect(row.getByText(t(`glossary.jobFunction.${member.jobFunction}`))).toBeInTheDocument();
  }
  expect(screen.getByRole('columnheader', { name: t('compliance.assign.learners.columns.name') })).toBeInTheDocument();
  expect(
    screen.getByRole('columnheader', { name: t('compliance.assign.learners.columns.jobFunction') }),
  ).toBeInTheDocument();
  expect(membersReads()).toContainEqual({ page: '1', pageSize: '20' });
});

test('the count of the people selected starts at none, and follows each checkbox', async () => {
  const user = userEvent.setup();
  await renderAssignPage();
  await toLearners(user);

  await liveCount(0);
  await user.click(await memberCheckbox(anna));
  await liveCount(1);
  await user.click(await memberCheckbox(ben));
  await liveCount(2);
  await user.click(await memberCheckbox(anna));

  await liveCount(1);
  expect(await memberCheckbox(anna)).not.toBeChecked();
  expect(await memberCheckbox(ben)).toBeChecked();
});

test('one button per job function offers to select everyone who has it', async () => {
  const user = userEvent.setup();
  await renderAssignPage();
  await toLearners(user);

  for (const jobFunction of ['Pharmacist', 'Pta', 'Pka', 'Trainee', 'Other'] as const) {
    expect(jobFunctionButton(jobFunction)).toBeInTheDocument();
  }
});

test('selecting everyone with a job function reads them by it and checks them', async () => {
  const user = userEvent.setup();
  await renderAssignPage();
  await toLearners(user);

  await user.click(jobFunctionButton('Pta'));

  await liveCount(2);
  expect(await memberCheckbox(ben)).toBeChecked();
  expect(await memberCheckbox(carla)).toBeChecked();
  expect(await memberCheckbox(anna)).not.toBeChecked();
  expect(await memberCheckbox(david)).not.toBeChecked();
  expect(membersReads()).toContainEqual({ page: '1', pageSize: '100', jobFunction: 'Pta' });
});

test('selecting by job function adds to the people already selected', async () => {
  const user = userEvent.setup();
  await renderAssignPage();
  await toLearners(user);
  await user.click(await memberCheckbox(anna));

  await user.click(jobFunctionButton('Pta'));

  await liveCount(3);
  expect(await memberCheckbox(anna)).toBeChecked();
});

// The host answers at most 100 a page (PageSize.Maximum): the button reads page after page until it has them all,
// beyond the page of the table shown.
test('selecting everyone with a job function reads every page of them', async () => {
  const user = userEvent.setup();
  const trainees = staff(150, 'Trainee', 'Azubi');
  state.members[markt.id] = [...team, ...trainees];
  await renderAssignPage();
  await toLearners(user);

  await user.click(jobFunctionButton('Trainee'));

  await liveCount(150);
  const reads = membersReads().filter((read) => read.jobFunction === 'Trainee');
  expect(reads).toEqual([
    { page: '1', pageSize: '100', jobFunction: 'Trainee' },
    { page: '2', pageSize: '100', jobFunction: 'Trainee' },
  ]);
});

// Follow-ups of #37 (#239): more than 100 can never be assigned at once, so reading stops at the page that passes the
// limit, and the step says so at once rather than at Next.
test('selecting everyone with a job function stops reading at the page that passes 100, and refuses at once', async () => {
  const user = userEvent.setup();
  state.members[markt.id] = [...team, ...staff(250, 'Trainee', 'Azubi')];
  await renderAssignPage();
  await toLearners(user);

  await user.click(jobFunctionButton('Trainee'));

  const alert = await screen.findByRole('alert');
  expect(alert).toHaveTextContent(t('compliance.assign.learners.tooMany', { max: 100 }));
  await waitFor(() => expect(alert).toHaveFocus());
  expect(membersReads().filter((read) => read.jobFunction === 'Trainee')).toEqual([
    { page: '1', pageSize: '100', jobFunction: 'Trainee' },
    { page: '2', pageSize: '100', jobFunction: 'Trainee' },
  ]);
});

test('Clear empties the selection', async () => {
  const user = userEvent.setup();
  await renderAssignPage();
  await toLearners(user);
  await user.click(await memberCheckbox(anna));
  await user.click(jobFunctionButton('Pta'));
  await liveCount(3);

  await user.click(screen.getByRole('button', { name: t('compliance.assign.learners.clear') }));

  await liveCount(0);
  for (const member of team) {
    expect(await memberCheckbox(member)).not.toBeChecked();
  }
});

test('Next with nobody selected is refused with an alert that takes the focus', async () => {
  const user = userEvent.setup();
  await renderAssignPage();
  await toLearners(user);

  await user.click(nextButton());

  const alert = await screen.findByRole('alert');
  expect(alert).toHaveTextContent(t('compliance.assign.learners.required'));
  await waitFor(() => expect(alert).toHaveFocus());
  expect(screen.getByRole('heading', { level: 2, name: t('compliance.assign.learners.title') })).toBeInTheDocument();
});

test('Next with more than 100 selected is refused with an alert that takes the focus', async () => {
  const user = userEvent.setup();
  state.members[markt.id] = [...team, ...staff(101, 'Other', 'Aushilfe')];
  await renderAssignPage();
  await toLearners(user);
  await user.click(jobFunctionButton('Other'));
  await liveCount(101);

  await user.click(nextButton());

  const alert = await screen.findByRole('alert');
  expect(alert).toHaveTextContent(t('compliance.assign.learners.tooMany', { max: 100 }));
  await waitFor(() => expect(alert).toHaveFocus());
  expect(screen.queryByRole('heading', { name: t('compliance.assign.due.title') })).not.toBeInTheDocument();
});

test('Next with exactly 100 selected goes on to the due date', async () => {
  const user = userEvent.setup();
  state.members[markt.id] = staff(100, 'Pta', 'PTA');
  await renderAssignPage();
  await toLearners(user);
  await user.click(jobFunctionButton('Pta'));
  await liveCount(100);

  await user.click(nextButton());

  expect(await stepHeading('compliance.assign.due.title')).toBeInTheDocument();
});

test('a pharmacy with no members says so and leads to the members page', async () => {
  const user = userEvent.setup();
  state.members[markt.id] = [];
  await renderAssignPage();
  await toLearners(user);

  expect(await screen.findByText(t('compliance.assign.learners.empty'))).toBeInTheDocument();
  expect(screen.getByRole('link', { name: t('compliance.assign.learners.invite') })).toHaveAttribute(
    'href',
    '/organisation',
  );
});

test('the selection is kept when the user goes back to the course step and on again', async () => {
  const user = userEvent.setup();
  await renderAssignPage();
  await toLearners(user);
  await user.click(await memberCheckbox(ben));

  await user.click(screen.getByRole('button', { name: t('compliance.assign.back') }));
  await stepHeading('compliance.assign.course.title');
  await user.click(nextButton());
  await stepHeading('compliance.assign.learners.title');

  expect(await memberCheckbox(ben)).toBeChecked();
  await liveCount(1);
});

// #479: the members are read again while the step shows, and a member who joined meanwhile can come before the others.
test("a member's checkbox stays that member's when the members are read again with one before them", async () => {
  const user = userEvent.setup();
  const { queryClient } = await renderAssignPage();
  await toLearners(user);
  const checkbox = await memberCheckbox(ben);

  const aaron = { ...anna, personId: '0198c1a0-0000-7000-8000-0000000000e5', name: 'Aaron Adler' };
  state.members = { ...state.members, [markt.id]: [aaron, ...team] };
  await queryClient.invalidateQueries();
  await memberCheckbox(aaron);

  expect(checkbox).toHaveAccessibleName(t('compliance.assign.learners.select', { name: ben.name }));
  await user.click(checkbox);
  await liveCount(1);
  expect(await memberCheckbox(ben)).toBeChecked();
  expect(await memberCheckbox(aaron)).not.toBeChecked();
  expect(await memberCheckbox(anna)).not.toBeChecked();
});
