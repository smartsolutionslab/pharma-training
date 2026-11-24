import { cleanup, screen, waitFor, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import type { PharmacyMemberDto } from '@/api/generated';
import { setLanguage, t } from '@/i18n';
import {
  anna,
  answering,
  changeButton,
  type Fetch,
  fixClock,
  markt,
  membersPath,
  minePath,
  never,
  now,
  problem,
  readsOf,
  renderTeamPage,
  rowOf,
  type TeamHost,
  teamHost,
  writesOf,
  you,
} from './teamFixtures';
import { titleKey } from './titleKey';

// Changing a membership's job function and role (#12): a dialog preset from the row, which PATCHes both. The last
// active manager cannot be made an employee (409 last-manager): the dialog then stays open, says so inside it and in a
// toast. Changing the caller's own membership reads their memberships again, since their capabilities may change.

const notify = vi.hoisted(() => ({ success: vi.fn(), failure: vi.fn() }));
vi.mock('@/lib/notify', () => ({ notify }));

const fetch = vi.fn<Fetch>();
vi.stubGlobal('fetch', fetch);

let host: TeamHost;

beforeEach(() => {
  localStorage.clear();
  fixClock();
  host = teamHost();
  fetch.mockImplementation(answering(host));
  notify.success.mockReset();
  notify.failure.mockReset();
});
afterEach(() => {
  fetch.mockReset();
  vi.useRealTimers();
  localStorage.clear();
});
// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(cleanup);

const memberPath = (member: PharmacyMemberDto) => `${membersPath}/${member.membershipId}`;
const memberReads = () => readsOf(fetch, membersPath).length;
const mineReads = () => readsOf(fetch, minePath).length;

/** The page with the change dialog open on a member's row. */
async function openChange(member: PharmacyMemberDto) {
  const user = userEvent.setup();
  const rendered = await renderTeamPage();
  await user.click(await changeButton(member));
  const element = await screen.findByRole('dialog', { name: t('organisation.team.change.title') });
  const dialog = within(element);
  return {
    user,
    element,
    dialog,
    jobFunction: dialog.getByRole('combobox', { name: t('organisation.team.invite.jobFunction') }),
    role: dialog.getByRole('combobox', { name: t('organisation.team.invite.role') }),
    submit: dialog.getByRole('button', { name: t('organisation.team.change.submit') }),
    ...rendered,
  };
}

test('the dialog names the member and the pharmacy and is preset from the row', async () => {
  const { element, jobFunction, role, submit } = await openChange(anna);

  expect(element).toHaveAccessibleDescription(
    t('organisation.team.change.description', { name: anna.name, pharmacy: markt.name }),
  );
  expect(jobFunction).toHaveValue('Pta');
  expect(role).toHaveValue('Employee');
  expect(submit).toBeEnabled();
});

test('opens with the focus in its first field', async () => {
  const { jobFunction } = await openChange(anna);

  await waitFor(() => expect(jobFunction).toHaveFocus());
});

test('the dialog on the caller’s own row is preset from it', async () => {
  const { jobFunction, role } = await openChange(you);

  expect(jobFunction).toHaveValue('Pharmacist');
  expect(role).toHaveValue('Manager');
});

test('saved, PATCHes both fields, closes, reports it and shows the new job function', async () => {
  const { user, jobFunction, role, submit } = await openChange(anna);
  const before = memberReads();

  await user.selectOptions(jobFunction, t('glossary.jobFunction.Pka'));
  await user.selectOptions(role, t('glossary.membershipRole.Manager'));
  await user.click(submit);

  await waitFor(() =>
    expect(notify.success).toHaveBeenCalledWith(t('organisation.team.change.changed', { name: anna.name })),
  );
  expect(writesOf(fetch)).toEqual([
    { method: 'PATCH', path: memberPath(anna), body: { jobFunction: 'Pka', role: 'Manager' } },
  ]);
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  await waitFor(() => expect(memberReads()).toBeGreaterThan(before));
  const row = await rowOf(anna.name);
  await waitFor(() => expect(row.getByText(t('glossary.jobFunction.Pka'))).toBeInTheDocument());
  expect(row.getByText(t('glossary.membershipRole.Manager'))).toBeInTheDocument();
});

// The host does nothing for an unchanged membership; the page still sends both fields as they stand.
test('saved unchanged, sends both fields as they stand', async () => {
  const { user, submit } = await openChange(anna);

  await user.click(submit);

  await waitFor(() =>
    expect(writesOf(fetch)).toEqual([
      { method: 'PATCH', path: memberPath(anna), body: { jobFunction: 'Pta', role: 'Employee' } },
    ]),
  );
});

test('the button is disabled while the host has not answered, so a change is sent once', async () => {
  // The form validates for a few milliseconds before it sends (vee-validate's debounce), while the button is disabled
  // at once: wait for the one write, then let another validation window pass and check no second write followed.
  vi.useFakeTimers({ toFake: ['Date', 'setTimeout', 'clearTimeout'], shouldAdvanceTime: true });
  vi.setSystemTime(now);
  host.change = never;
  const { user, submit } = await openChange(anna);

  await user.dblClick(submit);

  await waitFor(() => expect(writesOf(fetch)).toHaveLength(1));
  await vi.advanceTimersByTimeAsync(100);
  expect(writesOf(fetch)).toHaveLength(1);
  expect(submit).toBeDisabled();
});

test('the last manager made an employee stays open, says why in it and in a toast, and keeps the choice', async () => {
  host.change = async () =>
    problem(409, 'organisation.last-manager', 'The last active manager 0198c1a0-… cannot be changed.');
  const { user, dialog, role, submit } = await openChange(you);

  await user.selectOptions(role, t('glossary.membershipRole.Employee'));
  await user.click(submit);

  const message = t('errors.organisation.last-manager');
  expect(await dialog.findByRole('alert')).toHaveTextContent(message);
  expect(notify.failure).toHaveBeenCalledWith(message);
  expect(notify.success).not.toHaveBeenCalled();
  expect(screen.getByRole('dialog')).toBeVisible();
  expect(role).toHaveValue('Employee');
  expect(submit).toBeEnabled();
  expect(dialog.getByRole('alert')).not.toHaveTextContent('0198c1a0');
});

test('a membership no longer active is reported in the dialog and the members are read again', async () => {
  host.change = async (membershipId) => {
    host.members = host.members.filter((member) => member.membershipId !== membershipId);
    return problem(404, 'organisation.no-active-membership', 'x');
  };
  const { user, dialog, submit } = await openChange(anna);
  const before = memberReads();

  await user.click(submit);

  expect(await dialog.findByRole('alert')).toHaveTextContent(t('errors.organisation.no-active-membership'));
  await waitFor(() => expect(memberReads()).toBeGreaterThan(before));
});

test('failing for another reason stays open and says the change could not be made', async () => {
  host.change = async () => new Response('', { status: 500 });
  const { user, dialog, submit } = await openChange(anna);

  await user.click(submit);

  const message = t('organisation.team.change.failed', { name: anna.name });
  expect(await dialog.findByRole('alert')).toHaveTextContent(message);
  expect(notify.failure).toHaveBeenCalledWith(message);
});

// The caller's capabilities follow their role: the shell reads the memberships again to know them.
test('changing the caller’s own membership reads their memberships again', async () => {
  host.members = [
    you,
    anna,
    { ...anna, membershipId: '0198c1a0-0000-7000-8000-0000000000b5', name: 'Bea Brandt', role: 'Manager' },
  ];
  const { user, jobFunction, submit } = await openChange(you);
  const before = mineReads();

  await user.selectOptions(jobFunction, t('glossary.jobFunction.Other'));
  await user.click(submit);

  await waitFor(() =>
    expect(notify.success).toHaveBeenCalledWith(t('organisation.team.change.changed', { name: you.name })),
  );
  await waitFor(() => expect(mineReads()).toBeGreaterThan(before));
});

// Demoted, the caller may no longer manage members: the sections and the dialog go, and the focus, rather than falling
// to the body, goes to the page's heading, above the words that say why.
test('demoting oneself while another manager remains moves the focus to the page’s heading', async () => {
  host.members = [
    you,
    anna,
    { ...anna, membershipId: '0198c1a0-0000-7000-8000-0000000000b5', name: 'Bea Brandt', role: 'Manager' },
  ];
  const change = host.change;
  host.change = async (membershipId, body) => {
    const answer = await change(membershipId, body);
    host.memberships = host.memberships.map((membership) => ({ ...membership, canManageMembers: false }));
    return answer;
  };
  const { user, role, submit } = await openChange(you);

  await user.selectOptions(role, t('glossary.membershipRole.Employee'));
  await user.click(submit);

  expect(await screen.findByText(t('organisation.team.notAllowed', { pharmacy: markt.name }))).toBeInTheDocument();
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  await waitFor(() => expect(screen.getByRole('heading', { level: 1, name: t(titleKey) })).toHaveFocus());
});

test('cancelled, sends nothing', async () => {
  const { user, dialog } = await openChange(anna);

  await user.click(dialog.getByRole('button', { name: t('organisation.team.cancel') }));

  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  expect(writesOf(fetch)).toEqual([]);
});

test('asks in English once switched', async () => {
  setLanguage('en-GB', { remember: false });
  const { element, dialog } = await openChange(anna);

  expect(element).toHaveAccessibleName('Change job function and role');
  expect(element).toHaveAccessibleDescription('Anna Albers in Apotheke am Markt');
  expect(dialog.getByRole('button', { name: 'Save' })).toBeEnabled();
});
