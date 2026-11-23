import { cleanup, screen, waitFor, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { createPinia } from 'pinia';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import type { MyPharmacyDto } from '@/api/generated';
import { t } from '@/i18n';
import { useSelectedPharmacyStore } from '@/stores/selectedPharmacy';
import {
  answering,
  anna,
  ben,
  changeButton,
  endButton,
  type Fetch,
  fixClock,
  invitationsSection,
  inviteButton,
  markt,
  membersSection,
  minePath,
  problem,
  readsOf,
  renderTeamPage,
  revokeButton,
  rowOf,
  type TeamHost,
  teamHost,
  writesOf,
  you,
} from './teamFixtures';

// A refused revoke or end stays under its section's heading for the pharmacy it was refused in. Switched to another
// pharmacy, the page shows that pharmacy's members and invitations, and a refusal that names the first one's member or
// invitation goes with it.

const notify = vi.hoisted(() => ({ success: vi.fn(), failure: vi.fn() }));
vi.mock('@/lib/notify', () => ({ notify }));

const fetch = vi.fn<Fetch>();
vi.stubGlobal('fetch', fetch);

/** A second pharmacy the caller manages, with no members or invitations the page would list. */
const ufer: MyPharmacyDto = {
  ...markt,
  id: '0198c1a0-0000-7000-8000-0000000000a2',
  name: 'Apotheke am Ufer',
  membershipId: '0198c1a0-0000-7000-8000-0000000000c1',
};

const emptyPage = () => Response.json({ items: [], totalCount: 0, page: 1, pageSize: 100 });

let host: TeamHost;

beforeEach(() => {
  localStorage.clear();
  fixClock();
  host = teamHost();
  host.memberships = [markt, ufer];
  const markets = answering(host);
  fetch.mockImplementation(async (input, init) => {
    const url = new URL(String(input), 'http://localhost');
    if ((init?.method ?? 'GET') === 'GET' && url.pathname.startsWith(`/api/organisation/pharmacies/${ufer.id}/`)) {
      return emptyPage();
    }
    return markets(input, init);
  });
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

/** The page on the Apotheke am Markt, chosen among the caller's two pharmacies. */
async function renderOnMarkt() {
  const pinia = createPinia();
  const store = useSelectedPharmacyStore(pinia);
  store.choose(markt.id);
  const rendered = await renderTeamPage({ pinia });
  return { user: userEvent.setup(), store, ...rendered };
}

async function confirmIn(name: string) {
  const dialog = within(await screen.findByRole('dialog'));
  return dialog.getByRole('button', { name });
}

test('a refused end on one pharmacy is gone once the page shows another', async () => {
  host.end = async () => problem(409, 'organisation.last-manager', 'x');
  const { user, store } = await renderOnMarkt();
  await user.click(await endButton(you));
  await user.click(await confirmIn(t('organisation.team.end.confirm')));
  const message = t('organisation.team.errors.lastManager');
  expect(await within(membersSection()).findByRole('alert')).toHaveTextContent(message);

  store.choose(ufer.id);

  expect(await screen.findByText(t('organisation.team.intro', { pharmacy: ufer.name }))).toBeInTheDocument();
  await screen.findByText(t('organisation.team.members.empty'));
  expect(within(membersSection()).queryByRole('alert')).not.toBeInTheDocument();
  expect(screen.queryByText(message)).not.toBeInTheDocument();
});

test('a refused revoke on one pharmacy is gone once the page shows another', async () => {
  host.revoke = async () => problem(404, 'organisation.no-open-invitation', 'x');
  const { user, store } = await renderOnMarkt();
  await user.click(await revokeButton(ben));
  await user.click(await confirmIn(t('organisation.team.revoke.confirm')));
  const message = t('organisation.team.errors.noOpenInvitation');
  expect(await within(invitationsSection()).findByRole('alert')).toHaveTextContent(message);

  store.choose(ufer.id);

  expect(await screen.findByText(t('organisation.team.intro', { pharmacy: ufer.name }))).toBeInTheDocument();
  await screen.findByText(t('organisation.team.invitations.empty'));
  expect(within(invitationsSection()).queryByRole('alert')).not.toBeInTheDocument();
  expect(screen.queryByText(message)).not.toBeInTheDocument();
});

// A refusal answered only after the switch belongs to the pharmacy the action was taken in, not to the one now shown.
test('an end refused only after the page switched to another pharmacy is not shown there', async () => {
  let answer: (response: Response) => void = () => undefined;
  host.end = () => new Promise<Response>((resolve) => (answer = resolve));
  const { user, store } = await renderOnMarkt();
  await user.click(await endButton(you));
  await user.click(await confirmIn(t('organisation.team.end.confirm')));

  store.choose(ufer.id);
  await screen.findByText(t('organisation.team.members.empty'));
  answer(problem(409, 'organisation.last-manager', 'x'));

  await vi.waitFor(() => expect(notify.failure).toHaveBeenCalled());
  // The confirmation closes only once end has returned: whatever it would show is shown by then.
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  expect(within(membersSection()).queryByRole('alert')).not.toBeInTheDocument();
  expect(screen.queryByText(t('organisation.team.errors.lastManager'))).not.toBeInTheDocument();
});

test('a revoke refused only after the page switched to another pharmacy is not shown there', async () => {
  let answer: (response: Response) => void = () => undefined;
  host.revoke = () => new Promise<Response>((resolve) => (answer = resolve));
  const { user, store } = await renderOnMarkt();
  await user.click(await revokeButton(ben));
  await user.click(await confirmIn(t('organisation.team.revoke.confirm')));

  store.choose(ufer.id);
  await screen.findByText(t('organisation.team.invitations.empty'));
  answer(problem(404, 'organisation.no-open-invitation', 'x'));

  await vi.waitFor(() => expect(notify.failure).toHaveBeenCalled());
  // The confirmation closes only once revoke has returned: whatever it would show is shown by then.
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  expect(within(invitationsSection()).queryByRole('alert')).not.toBeInTheDocument();
  expect(screen.queryByText(t('organisation.team.errors.noOpenInvitation'))).not.toBeInTheDocument();
});

// A dialog opened on one pharmacy asks about that pharmacy: switched to another, it closes, so it never names the other
// pharmacy, never acts on it and never shows what the first one's host answered.

/** Whether the page shows the Apotheke am Ufer, with its (empty) lists. */
async function showingUfer() {
  expect(await screen.findByText(t('organisation.team.intro', { pharmacy: ufer.name }))).toBeInTheDocument();
  await screen.findByText(t('organisation.team.members.empty'));
}

/** The requests that changed something at the Apotheke am Ufer. */
const uferWrites = () => writesOf(fetch).filter(({ path }) => path.includes(ufer.id));

test('an invitation refused only after the page switched to another pharmacy is not shown there', async () => {
  let answer: (response: Response) => void = () => undefined;
  host.invite = () => new Promise<Response>((resolve) => (answer = resolve));
  const { user, store } = await renderOnMarkt();
  await waitFor(() => expect(inviteButton()).toBeEnabled());
  await user.click(inviteButton());
  const dialog = within(await screen.findByRole('dialog', { name: t('organisation.team.invite.title') }));
  await user.click(dialog.getByRole('textbox', { name: t('organisation.team.invite.email') }));
  await user.paste('dora@example.com');
  await user.selectOptions(
    dialog.getByRole('combobox', { name: t('organisation.team.invite.jobFunction') }),
    t('glossary.jobFunction.Pta'),
  );
  await user.click(dialog.getByRole('button', { name: t('organisation.team.invite.submit') }));

  store.choose(ufer.id);
  await showingUfer();
  answer(problem(409, 'organisation.already-member-or-invited', 'x'));

  await vi.waitFor(() => expect(notify.failure).toHaveBeenCalled());
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  expect(screen.queryByText(t('organisation.team.errors.alreadyMemberOrInvited'))).not.toBeInTheDocument();
  expect(uferWrites()).toEqual([]);
});

test('a change refused only after the page switched to another pharmacy is not shown there', async () => {
  let answer: (response: Response) => void = () => undefined;
  host.change = () => new Promise<Response>((resolve) => (answer = resolve));
  const { user, store } = await renderOnMarkt();
  await user.click(await changeButton(anna));
  const dialog = within(await screen.findByRole('dialog', { name: t('organisation.team.change.title') }));
  await user.click(dialog.getByRole('button', { name: t('organisation.team.change.submit') }));

  store.choose(ufer.id);
  await showingUfer();
  answer(problem(409, 'organisation.last-manager', 'x'));

  await vi.waitFor(() => expect(notify.failure).toHaveBeenCalled());
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  expect(
    screen.queryByText(t('organisation.team.change.description', { name: anna.name, pharmacy: ufer.name })),
  ).not.toBeInTheDocument();
  expect(screen.queryByText(t('organisation.team.errors.lastManager'))).not.toBeInTheDocument();
  expect(uferWrites()).toEqual([]);
});

test('an open change dialog closes once the page shows another pharmacy', async () => {
  const { user, store } = await renderOnMarkt();
  await user.click(await changeButton(anna));
  await screen.findByRole('dialog', { name: t('organisation.team.change.title') });

  store.choose(ufer.id);
  await showingUfer();

  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  expect(uferWrites()).toEqual([]);
});

test('an open end confirmation closes once the page shows another pharmacy and never names it', async () => {
  const { user, store } = await renderOnMarkt();
  await user.click(await endButton(anna));
  await screen.findByRole('dialog', { name: t('organisation.team.end.title', { name: anna.name }) });

  store.choose(ufer.id);
  await showingUfer();

  expect(
    screen.queryByText(t('organisation.team.end.description', { name: anna.name, pharmacy: ufer.name })),
  ).not.toBeInTheDocument();
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  expect(uferWrites()).toEqual([]);
});

// Ending one's own membership goes to the start page, but only from the pharmacy it ended: switched to another, the
// page stays on that one, and the memberships are read again all the same.
test('an own membership ended only after the page switched to another pharmacy stays on that pharmacy', async () => {
  let answer: (response: Response) => void = () => undefined;
  host.end = () => new Promise<Response>((resolve) => (answer = resolve));
  const { user, store, router, queryClient } = await renderOnMarkt();
  const push = vi.spyOn(router, 'push');
  await user.click(await endButton(you));
  await user.click(await confirmIn(t('organisation.team.end.confirm')));

  store.choose(ufer.id);
  await showingUfer();
  const before = readsOf(fetch, minePath).length;
  host.memberships = [ufer];
  answer(new Response(null, { status: 200 }));

  await vi.waitFor(() =>
    expect(notify.success).toHaveBeenCalledWith(t('organisation.team.end.endedSelf', { pharmacy: markt.name })),
  );
  await waitFor(() => expect(readsOf(fetch, minePath).length).toBeGreaterThan(before));
  await waitFor(() => expect(queryClient.isFetching()).toBe(0));
  expect(push).not.toHaveBeenCalled();
  expect(router.currentRoute.value.path).toBe('/organisation');
  expect(await screen.findByText(t('organisation.team.intro', { pharmacy: ufer.name }))).toBeInTheDocument();
});

// A refusal answered after a switch still says which pharmacy refused, so its toast over another one is not mistaken
// for that one's.
test('the toast of a revoke refused only after the page switched names the pharmacy that refused it', async () => {
  let answer: (response: Response) => void = () => undefined;
  host.revoke = () => new Promise<Response>((resolve) => (answer = resolve));
  const { user, store } = await renderOnMarkt();
  await user.click(await revokeButton(ben));
  await user.click(await confirmIn(t('organisation.team.revoke.confirm')));

  store.choose(ufer.id);
  await showingUfer();
  answer(problem(404, 'organisation.no-open-invitation', 'x'));

  await vi.waitFor(() => expect(notify.failure).toHaveBeenCalled());
  expect(notify.failure).toHaveBeenCalledWith(
    'In Apotheke am Markt: Diese Einladung ist nicht mehr offen: Sie wurde angenommen oder zurückgezogen.',
  );
});

// Switched away and back before the host answers, the page shows the pharmacy the action was taken in again: its
// refusal shows there under its section's heading, as if nothing had been switched.
test('an end refused after switching to another pharmacy and back shows under the members again', async () => {
  let answer: (response: Response) => void = () => undefined;
  host.end = () => new Promise<Response>((resolve) => (answer = resolve));
  const { user, store } = await renderOnMarkt();
  await user.click(await endButton(you));
  await user.click(await confirmIn(t('organisation.team.end.confirm')));

  store.choose(ufer.id);
  await showingUfer();
  store.choose(markt.id);
  expect(await screen.findByText(t('organisation.team.intro', { pharmacy: markt.name }))).toBeInTheDocument();
  await rowOf(anna.name);
  answer(problem(409, 'organisation.last-manager', 'x'));

  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  expect(await within(membersSection()).findByRole('alert')).toHaveTextContent(
    t('organisation.team.errors.lastManager'),
  );
});

test('a revoke refused after switching to another pharmacy and back shows under the invitations again', async () => {
  let answer: (response: Response) => void = () => undefined;
  host.revoke = () => new Promise<Response>((resolve) => (answer = resolve));
  const { user, store } = await renderOnMarkt();
  await user.click(await revokeButton(ben));
  await user.click(await confirmIn(t('organisation.team.revoke.confirm')));

  store.choose(ufer.id);
  await showingUfer();
  store.choose(markt.id);
  expect(await screen.findByText(t('organisation.team.intro', { pharmacy: markt.name }))).toBeInTheDocument();
  await rowOf(ben.email);
  answer(problem(404, 'organisation.no-open-invitation', 'x'));

  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  expect(await within(invitationsSection()).findByRole('alert')).toHaveTextContent(
    t('organisation.team.errors.noOpenInvitation'),
  );
  expect(notify.failure).toHaveBeenCalledWith(t('organisation.team.errors.noOpenInvitation'));
});
