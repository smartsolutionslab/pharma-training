import { cleanup, screen, waitFor, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import type { PharmacyMemberDto } from '@/api/generated';
import { setLanguage, t } from '@/i18n';
import {
  anna,
  answering,
  endButton,
  type Fetch,
  fixClock,
  markt,
  membersHeading,
  membersPath,
  membersSection,
  minePath,
  never,
  problem,
  readsOf,
  renderTeamPage,
  rowOf,
  type TeamHost,
  teamHost,
  writesOf,
  you,
} from './teamFixtures';

// Ending a membership (#12) asks first. Ending another member's reports it and takes the row out; ending the caller's
// own says they are no longer a member, reads their memberships again and goes to the start page. The last active
// manager cannot be ended (409 last-manager): the confirmation closes, and the refusal shows in a toast and under the
// section's heading, where it stays readable.

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

/** The page with the confirmation to end a member's membership open. */
async function openEnd(member: PharmacyMemberDto) {
  const user = userEvent.setup();
  const rendered = await renderTeamPage();
  await user.click(await endButton(member));
  const element = await screen.findByRole('dialog', { name: t('organisation.team.end.title', { name: member.name }) });
  const dialog = within(element);
  return {
    user,
    element,
    dialog,
    confirm: dialog.getByRole('button', { name: t('organisation.team.end.confirm') }),
    cancel: dialog.getByRole('button', { name: t('organisation.team.cancel') }),
    ...rendered,
  };
}

test('ending another member’s membership asks first, saying what it means, with the focus on Cancel', async () => {
  const { element, confirm, cancel } = await openEnd(anna);

  expect(element).toHaveAccessibleName('Anna Albers austragen?');
  expect(element).toHaveAccessibleDescription(
    t('organisation.team.end.description', { name: anna.name, pharmacy: markt.name }),
  );
  expect(confirm).toBeEnabled();
  await waitFor(() => expect(cancel).toHaveFocus());
});

test('cancelled, sends nothing and keeps the member', async () => {
  const { user, cancel } = await openEnd(anna);

  await user.click(cancel);

  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  expect(writesOf(fetch)).toEqual([]);
  expect(await rowOf(anna.name)).toBeTruthy();
});

test('confirmed, deletes it, reports it, takes the row out and puts the focus on the section’s heading', async () => {
  const { user, confirm, router } = await openEnd(anna);
  const before = memberReads();

  await user.click(confirm);

  await waitFor(() =>
    expect(notify.success).toHaveBeenCalledWith(t('organisation.team.end.ended', { name: anna.name })),
  );
  expect(writesOf(fetch)).toEqual([{ method: 'DELETE', path: memberPath(anna), body: undefined }]);
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  await waitFor(() => expect(memberReads()).toBeGreaterThan(before));
  await waitFor(() => expect(screen.queryByText(anna.name)).not.toBeInTheDocument());
  await waitFor(() => expect(membersHeading()).toHaveFocus());
  expect(router.currentRoute.value.path).toBe('/organisation');
});

test('disables its confirmation while the host has not answered', async () => {
  host.end = never;
  const { user, confirm } = await openEnd(anna);

  await user.click(confirm);

  await waitFor(() => expect(confirm).toBeDisabled());
  expect(writesOf(fetch)).toHaveLength(1);
});

test('ending the caller’s own membership says they will no longer be a member', async () => {
  const { element } = await openEnd(you);

  expect(element).toHaveAccessibleDescription(t('organisation.team.end.descriptionSelf', { pharmacy: markt.name }));
});

test('confirmed on the caller’s own row, says so, reads their memberships again and goes to the start page', async () => {
  host.members = [
    you,
    anna,
    { ...anna, membershipId: '0198c1a0-0000-7000-8000-0000000000b5', name: 'Bea Brandt', role: 'Manager' },
  ];
  const { user, confirm, router } = await openEnd(you);
  const before = mineReads();

  await user.click(confirm);

  await waitFor(() =>
    expect(notify.success).toHaveBeenCalledWith(t('organisation.team.end.endedSelf', { pharmacy: markt.name })),
  );
  expect(writesOf(fetch)).toEqual([{ method: 'DELETE', path: memberPath(you), body: undefined }]);
  await waitFor(() => expect(mineReads()).toBeGreaterThan(before));
  await waitFor(() => expect(router.currentRoute.value.path).toBe('/'));
});

test('the last manager ended closes, says why in a toast and under the heading, and stays listed', async () => {
  host.end = async () => problem(409, 'organisation.last-manager', 'The last active manager cannot be removed.');
  const { user, confirm, router } = await openEnd(you);
  const before = memberReads();

  await user.click(confirm);

  const message = t('organisation.team.errors.lastManager');
  await waitFor(() => expect(notify.failure).toHaveBeenCalledWith(message));
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  const alert = await within(membersSection()).findByRole('alert');
  expect(alert).toHaveTextContent(message);
  expect(alert).not.toHaveTextContent('The last active manager');
  expect(await rowOf(t('organisation.team.members.you', { name: you.name }))).toBeTruthy();
  await waitFor(() => expect(memberReads()).toBeGreaterThan(before));
  expect(router.currentRoute.value.path).toBe('/organisation');
  expect(notify.success).not.toHaveBeenCalled();
});

test('the refusal under the heading goes once the next action succeeds', async () => {
  let refuse = true;
  const end = host.end;
  host.end = async (membershipId) => (refuse ? problem(409, 'organisation.last-manager', 'x') : end(membershipId));
  const { user, confirm } = await openEnd(you);
  await user.click(confirm);
  expect(await within(membersSection()).findByRole('alert')).toHaveTextContent(
    t('organisation.team.errors.lastManager'),
  );

  refuse = false;
  await user.click(await endButton(anna));
  await user.click(
    within(await screen.findByRole('dialog')).getByRole('button', { name: t('organisation.team.end.confirm') }),
  );

  await waitFor(() =>
    expect(notify.success).toHaveBeenCalledWith(t('organisation.team.end.ended', { name: anna.name })),
  );
  await waitFor(() => expect(within(membersSection()).queryByRole('alert')).not.toBeInTheDocument());
});

test('a membership no longer active is reported and the members are read again', async () => {
  host.end = async (membershipId) => {
    host.members = host.members.filter((member) => member.membershipId !== membershipId);
    return problem(404, 'organisation.no-active-membership', 'x');
  };
  const { user, confirm } = await openEnd(anna);
  const before = memberReads();

  await user.click(confirm);

  await waitFor(() => expect(notify.failure).toHaveBeenCalledWith(t('organisation.team.errors.noActiveMembership')));
  await waitFor(() => expect(memberReads()).toBeGreaterThan(before));
  await waitFor(() => expect(screen.queryByText(anna.name)).not.toBeInTheDocument());
});

// The membership is gone or may no longer manage: the memberships are read again, so the shell hides the page.
test('a pharmacy the caller is no longer a member of reads their memberships again', async () => {
  host.end = async () => problem(404, 'organisation.pharmacy-not-found', 'x');
  const { user, confirm } = await openEnd(anna);
  const before = mineReads();

  await user.click(confirm);

  await waitFor(() => expect(notify.failure).toHaveBeenCalledWith(t('organisation.team.errors.pharmacyNotFound')));
  await waitFor(() => expect(mineReads()).toBeGreaterThan(before));
});

test('failing for another reason says the membership could not be ended', async () => {
  host.end = async () => new Response('', { status: 500 });
  const { user, confirm } = await openEnd(anna);

  await user.click(confirm);

  await waitFor(() =>
    expect(notify.failure).toHaveBeenCalledWith(t('organisation.team.end.failed', { name: anna.name })),
  );
});

test('asks in English once switched', async () => {
  setLanguage('en-GB', { remember: false });
  const { element, dialog } = await openEnd(anna);

  expect(element).toHaveAccessibleName('End the membership of Anna Albers?');
  expect(dialog.getByRole('button', { name: 'End membership' })).toBeEnabled();
  expect(dialog.getByRole('button', { name: 'Cancel' })).toBeEnabled();
});
