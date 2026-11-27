import { cleanup, screen, waitFor, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import type { PharmacyInvitationDto, PharmacyMemberDto } from '@/api/generated';
import { t } from '@/i18n';
import {
  anna,
  answering,
  ben,
  carla,
  changeButton,
  endButton,
  type Fetch,
  fixClock,
  invitationsPath,
  markt,
  membersPath,
  renderTeamPage,
  renewButton,
  revokeButton,
  type TeamHost,
  teamHost,
  writesOf,
} from './teamFixtures';

// #479: the members and invitations are read again after every action, and a row read meanwhile can come before the
// others (a member by name, an invitation by expiry). A row's buttons stay that row's: held before the list is read
// again, they still name and act on their own record afterwards.

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
});
afterEach(() => {
  fetch.mockReset();
  vi.useRealTimers();
  localStorage.clear();
});
// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(cleanup);

/** A member whose name sorts before every other's, so the host lists them first. */
const aaron: PharmacyMemberDto = {
  ...anna,
  membershipId: '0198c1a0-0000-7000-8000-0000000000b5',
  personId: '0198c1a0-0000-7000-8000-0000000000e5',
  name: 'Aaron Adler',
};

/** An invitation that expired before every other, so the host lists it first. */
const dora: PharmacyInvitationDto = {
  ...carla,
  membershipId: '0198c1a0-0000-7000-8000-0000000000b6',
  email: 'dora@example.com',
  expiresAt: '2026-11-20T12:00:00Z',
};

test("a member's Change button stays that member's when the members are read again with one before them", async () => {
  const user = userEvent.setup();
  const { queryClient } = await renderTeamPage();
  const change = await changeButton(anna);

  host.members = [...host.members, aaron];
  await queryClient.invalidateQueries();
  await changeButton(aaron);

  expect(change).toHaveAccessibleName(t('organisation.team.members.changeOf', { name: anna.name }));
  await user.click(change);
  const dialog = await screen.findByRole('dialog', { name: t('organisation.team.change.title') });
  expect(dialog).toHaveAccessibleDescription(
    t('organisation.team.change.description', { name: anna.name, pharmacy: markt.name }),
  );
});

test("a member's End button stays that member's when the members are read again with one before them", async () => {
  const user = userEvent.setup();
  const { queryClient } = await renderTeamPage();
  const end = await endButton(anna);

  host.members = [...host.members, aaron];
  await queryClient.invalidateQueries();
  await endButton(aaron);

  expect(end).toHaveAccessibleName(t('organisation.team.members.endOf', { name: anna.name }));
  await user.click(end);
  const dialog = await screen.findByRole('dialog', { name: t('organisation.team.end.title', { name: anna.name }) });
  await user.click(within(dialog).getByRole('button', { name: t('organisation.team.end.confirm') }));
  await waitFor(() =>
    expect(writesOf(fetch)).toEqual([{ method: 'DELETE', path: `${membersPath}/${anna.membershipId}` }]),
  );
});

test("an invitation's Resend button stays that invitation's when the list is read again with one before it", async () => {
  const user = userEvent.setup();
  const { queryClient } = await renderTeamPage();
  const renew = await renewButton(ben);

  host.invitations = [...host.invitations, dora];
  await queryClient.invalidateQueries();
  await renewButton(dora);

  expect(renew).toHaveAccessibleName(t('organisation.team.invitations.renewOf', { email: ben.email }));
  await user.click(renew);
  await waitFor(() =>
    expect(writesOf(fetch)).toEqual([{ method: 'POST', path: `${invitationsPath}/${ben.membershipId}/renew` }]),
  );
});

test("an invitation's Revoke button stays that invitation's when the list is read again with one before it", async () => {
  const user = userEvent.setup();
  const { queryClient } = await renderTeamPage();
  const revoke = await revokeButton(ben);

  host.invitations = [...host.invitations, dora];
  await queryClient.invalidateQueries();
  await revokeButton(dora);

  expect(revoke).toHaveAccessibleName(t('organisation.team.invitations.revokeOf', { email: ben.email }));
  await user.click(revoke);
  const dialog = await screen.findByRole('dialog', { name: t('organisation.team.revoke.title', { email: ben.email }) });
  await user.click(within(dialog).getByRole('button', { name: t('organisation.team.revoke.confirm') }));
  await waitFor(() =>
    expect(writesOf(fetch)).toEqual([{ method: 'DELETE', path: `${invitationsPath}/${ben.membershipId}` }]),
  );
});
