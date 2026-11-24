import { cleanup, screen, waitFor, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest';
import { setLanguage, t } from '@/i18n';
import {
  answering,
  ben,
  carla,
  type Fetch,
  fixClock,
  invitationsHeading,
  invitationsPath,
  invitationsSection,
  markt,
  never,
  now,
  problem,
  readsOf,
  renderTeamPage,
  renewButton,
  revokeButton,
  rowOf,
  type TeamHost,
  teamHost,
  writesOf,
} from './teamFixtures';

// The invitations (#12): the memberships still Invited, open and expired, by expiry, with the address, job function,
// role, the day they are valid until and their status. Expired means its expiry lies before now (the domain's strict
// comparison); the word carries the meaning, the colour only repeats it. Renewing (ADR 0004: 14 new days, the old
// link stops working, a new mail) needs no confirmation; revoking asks first. Each answer is reported, and the list is
// read again, after a refusal too.

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

const renewPath = (membershipId: string) => `${invitationsPath}/${membershipId}/renew`;
const revokePath = (membershipId: string) => `${invitationsPath}/${membershipId}`;
const invitationReads = () => readsOf(fetch, invitationsPath).length;

describe('the invitations table', () => {
  test('names its columns', async () => {
    await renderTeamPage();

    await rowOf(ben.email);
    const headers = within(invitationsSection())
      .getAllByRole('columnheader')
      .map((header) => header.textContent.trim());
    expect(headers).toEqual(
      expect.arrayContaining([
        t('organisation.team.columns.email'),
        t('organisation.team.columns.jobFunction'),
        t('organisation.team.columns.role'),
        t('organisation.team.columns.expires'),
        t('organisation.team.columns.status'),
      ]),
    );
  });

  test('lists each invitation with its job function, role and the day it is valid until', async () => {
    await renderTeamPage();

    const open = await rowOf(ben.email);
    expect(open.getByText(t('glossary.jobFunction.Pka'))).toBeInTheDocument();
    expect(open.getByText(t('glossary.membershipRole.Employee'))).toBeInTheDocument();
    expect(open.getByText('01.12.2026')).toBeInTheDocument();
    const expired = await rowOf(carla.email);
    expect(expired.getByText(t('glossary.jobFunction.Trainee'))).toBeInTheDocument();
    expect(expired.getByText('21.11.2026')).toBeInTheDocument();
  });

  // The clock stands at 2026-11-22T12:00Z.
  test('marks an invitation that expired yesterday as expired and one that expires next month as open', async () => {
    await renderTeamPage();

    const expired = await rowOf(carla.email);
    const expiredStatus = expired.getByText('Abgelaufen');
    expect(expiredStatus).toHaveClass('text-destructive');
    expect(expired.queryByText('Offen')).not.toBeInTheDocument();
    const open = await rowOf(ben.email);
    expect(open.getByText('Offen')).not.toHaveClass('text-destructive');
    expect(open.queryByText('Abgelaufen')).not.toBeInTheDocument();
  });

  test('an invitation at the very instant it expires is still open', async () => {
    host.invitations = [{ ...ben, expiresAt: '2026-11-22T12:00:00Z' }];
    await renderTeamPage();

    const row = await rowOf(ben.email);
    expect(row.getByText(t('organisation.team.invitations.open'))).toBeInTheDocument();
    expect(row.queryByText(t('organisation.team.invitations.expired'))).not.toBeInTheDocument();
  });

  // The status follows the clock while the page stays open: a minute's tick, not a refetch, turns Offen into Abgelaufen.
  test('an open invitation turns expired once its expiry passes while the page is open, without reading again', async () => {
    vi.useRealTimers();
    vi.useFakeTimers({
      toFake: ['Date', 'setTimeout', 'clearTimeout', 'setInterval', 'clearInterval'],
      shouldAdvanceTime: true,
    });
    vi.setSystemTime(now);
    host.invitations = [{ ...ben, expiresAt: new Date(now.getTime() + 30_000).toISOString() }];
    await renderTeamPage();
    const open = await rowOf(ben.email);
    expect(open.getByText('Offen')).toBeInTheDocument();
    const reads = invitationReads();

    await vi.advanceTimersByTimeAsync(61_000);

    await waitFor(async () => expect((await rowOf(ben.email)).getByText('Abgelaufen')).toBeInTheDocument());
    expect((await rowOf(ben.email)).queryByText('Offen')).not.toBeInTheDocument();
    expect(invitationReads()).toBe(reads);
  });

  test('says open and expired in English, with the dates as the English write them', async () => {
    setLanguage('en-GB', { remember: false });
    await renderTeamPage();

    const expired = await rowOf(carla.email);
    expect(expired.getByText('Expired')).toBeInTheDocument();
    expect(expired.getByText('21/11/2026')).toBeInTheDocument();
    const open = await rowOf(ben.email);
    expect(open.getByText('Open')).toBeInTheDocument();
    expect(open.getByText('01/12/2026')).toBeInTheDocument();
  });

  test('offers to resend and to revoke every invitation, open or expired', async () => {
    await renderTeamPage();

    for (const invitation of [ben, carla]) {
      expect(await renewButton(invitation)).toBeEnabled();
      expect(await revokeButton(invitation)).toBeEnabled();
    }
  });

  test('without invitations says so, and its action opens the invite dialog', async () => {
    const user = userEvent.setup();
    host.invitations = [];
    await renderTeamPage();

    const section = within(await waitFor(() => invitationsSection()));
    expect(await section.findByText(t('organisation.team.invitations.empty'))).toBeVisible();
    await user.click(section.getByRole('button', { name: t('organisation.team.invitations.inviteFirst') }));

    expect(await screen.findByRole('dialog', { name: t('organisation.team.invite.title') })).toBeVisible();
  });

  test('while the invitations load the table is busy', async () => {
    host.invitationsAnswer = never;
    await renderTeamPage();

    await waitFor(() => expect(invitationsSection().querySelector('[aria-busy="true"]')).not.toBeNull());
    expect(screen.queryByText(t('organisation.team.invitations.empty'))).not.toBeInTheDocument();
  });
});

describe('resending an invitation', () => {
  test('posts it at once, reports it and shows its new expiry', async () => {
    const user = userEvent.setup();
    await renderTeamPage();
    await rowOf(carla.email);
    const before = invitationReads();

    await user.click(await renewButton(carla));

    await waitFor(() =>
      expect(notify.success).toHaveBeenCalledWith(t('organisation.team.invitations.renewed', { email: carla.email })),
    );
    expect(writesOf(fetch)).toEqual([{ method: 'POST', path: renewPath(carla.membershipId), body: undefined }]);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    await waitFor(() => expect(invitationReads()).toBeGreaterThan(before));
    // 14 new days from 2026-11-22: open again until 2026-12-06.
    const row = await rowOf(carla.email);
    await waitFor(() => expect(row.getByText('06.12.2026')).toBeInTheDocument());
    expect(row.getByText(t('organisation.team.invitations.open'))).toBeInTheDocument();
  });

  test('disables the row’s button while the host has not answered, so it is sent once', async () => {
    const user = userEvent.setup();
    host.renew = never;
    await renderTeamPage();

    await user.click(await renewButton(ben));

    await waitFor(() =>
      expect(
        screen.getByRole('button', { name: t('organisation.team.invitations.renewOf', { email: ben.email }) }),
      ).toBeDisabled(),
    );
    await user.click(await renewButton(ben));
    expect(writesOf(fetch)).toHaveLength(1);
    expect(await renewButton(carla)).toBeEnabled();
  });

  test('refused because it is no longer open says so, and reads the list again', async () => {
    const user = userEvent.setup();
    host.renew = async (membershipId) => {
      host.invitations = host.invitations.filter((invitation) => invitation.membershipId !== membershipId);
      return problem(404, 'organisation.no-open-invitation', 'No open invitation 0198c1a0-….');
    };
    await renderTeamPage();
    await rowOf(ben.email);
    const before = invitationReads();

    await user.click(await renewButton(ben));

    await waitFor(() => expect(notify.failure).toHaveBeenCalledWith(t('organisation.team.errors.noOpenInvitation')));
    expect(notify.success).not.toHaveBeenCalled();
    await waitFor(() => expect(invitationReads()).toBeGreaterThan(before));
    await waitFor(() => expect(screen.queryByText(ben.email)).not.toBeInTheDocument());
  });

  test('failing for another reason says the invitation could not be resent, and the row stays', async () => {
    const user = userEvent.setup();
    host.renew = async () => new Response('', { status: 500 });
    await renderTeamPage();

    await user.click(await renewButton(ben));

    await waitFor(() =>
      expect(notify.failure).toHaveBeenCalledWith(t('organisation.team.invitations.renewFailed', { email: ben.email })),
    );
    expect(await rowOf(ben.email)).toBeTruthy();
  });
});

describe('revoking an invitation', () => {
  /** The page with the confirmation to revoke Ben's invitation open. */
  async function openRevoke() {
    const user = userEvent.setup();
    await renderTeamPage();
    await user.click(await revokeButton(ben));
    const dialog = await screen.findByRole('dialog', {
      name: t('organisation.team.revoke.title', { email: ben.email }),
    });
    return { user, dialog: within(dialog), element: dialog };
  }

  const confirm = (dialog: ReturnType<typeof within>) =>
    dialog.getByRole('button', { name: t('organisation.team.revoke.confirm') });
  const cancel = (dialog: ReturnType<typeof within>) =>
    dialog.getByRole('button', { name: t('organisation.team.cancel') });

  test('asks first, saying what revoking means, with the focus on Cancel', async () => {
    const { dialog, element } = await openRevoke();

    expect(element).toHaveAccessibleDescription(t('organisation.team.revoke.description'));
    expect(element).toHaveAccessibleName('Einladung an ben@example.com zurückziehen?');
    expect(confirm(dialog)).toBeEnabled();
    await waitFor(() => expect(cancel(dialog)).toHaveFocus());
  });

  test('cancelled, sends nothing and keeps the invitation', async () => {
    const { user, dialog } = await openRevoke();

    await user.click(cancel(dialog));

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(writesOf(fetch)).toEqual([]);
    expect(await rowOf(ben.email)).toBeTruthy();
  });

  test('confirmed, deletes it, reports it, takes the row out and puts the focus on the section’s heading', async () => {
    const { user, dialog } = await openRevoke();
    const before = invitationReads();

    await user.click(confirm(dialog));

    await waitFor(() =>
      expect(notify.success).toHaveBeenCalledWith(t('organisation.team.revoke.revoked', { email: ben.email })),
    );
    expect(writesOf(fetch)).toEqual([{ method: 'DELETE', path: revokePath(ben.membershipId), body: undefined }]);
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    await waitFor(() => expect(invitationReads()).toBeGreaterThan(before));
    await waitFor(() => expect(screen.queryByText(ben.email)).not.toBeInTheDocument());
    await waitFor(() => expect(invitationsHeading()).toHaveFocus());
  });

  test('disables its confirmation while the host has not answered', async () => {
    host.revoke = never;
    const { user, dialog } = await openRevoke();

    await user.click(confirm(dialog));

    await waitFor(() => expect(confirm(dialog)).toBeDisabled());
    expect(writesOf(fetch)).toHaveLength(1);
  });

  test('refused because it is no longer open closes, says so in a toast and under the heading, and reads again', async () => {
    host.revoke = async () => problem(404, 'organisation.no-open-invitation', 'Words of the host, not for the user.');
    const { user, dialog } = await openRevoke();
    const before = invitationReads();

    await user.click(confirm(dialog));

    await waitFor(() => expect(notify.failure).toHaveBeenCalledWith(t('organisation.team.errors.noOpenInvitation')));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(await within(invitationsSection()).findByRole('alert')).toHaveTextContent(
      t('organisation.team.errors.noOpenInvitation'),
    );
    await waitFor(() => expect(invitationReads()).toBeGreaterThan(before));
    expect(notify.success).not.toHaveBeenCalled();
  });

  // The refusal keeps what it is, not its words: switched to English, it says it in English.
  test('the refusal under the heading follows a switch of language', async () => {
    host.revoke = async () => problem(404, 'organisation.no-open-invitation', 'Words of the host, not for the user.');
    const { user, dialog } = await openRevoke();
    await user.click(confirm(dialog));
    expect(await within(invitationsSection()).findByRole('alert')).toHaveTextContent(
      'Diese Einladung ist nicht mehr offen',
    );

    setLanguage('en-GB', { remember: false });

    await waitFor(() =>
      expect(within(invitationsSection()).getByRole('alert')).toHaveTextContent(
        'This invitation is no longer open: it was accepted or revoked.',
      ),
    );
  });

  test('failing for another reason says the invitation could not be revoked', async () => {
    host.revoke = async () => new Response('', { status: 500 });
    const { user, dialog } = await openRevoke();

    await user.click(confirm(dialog));

    await waitFor(() =>
      expect(notify.failure).toHaveBeenCalledWith(t('organisation.team.revoke.failed', { email: ben.email })),
    );
  });

  test('asks in English once switched', async () => {
    setLanguage('en-GB', { remember: false });
    const { dialog, element } = await openRevoke();

    expect(element).toHaveAccessibleName('Revoke the invitation to ben@example.com?');
    expect(dialog.getByRole('button', { name: 'Revoke' })).toBeEnabled();
    expect(dialog.getByRole('button', { name: 'Cancel' })).toBeEnabled();
  });
});

// The pharmacy is the current one: every request goes to its address.
test('every request goes to the current pharmacy’s address', async () => {
  const user = userEvent.setup();
  await renderTeamPage();

  await user.click(await renewButton(ben));

  await waitFor(() => expect(writesOf(fetch)).toHaveLength(1));
  expect(writesOf(fetch)[0]?.path).toContain(`/api/organisation/pharmacies/${markt.id}/`);
});
