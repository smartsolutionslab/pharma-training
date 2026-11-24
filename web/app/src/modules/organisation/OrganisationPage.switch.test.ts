import { cleanup, screen, waitFor, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { QueryClient } from '@tanstack/vue-query';
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
  type User,
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
  const { user, store, queryClient } = await renderOnMarkt();
  await user.click(await endButton(you));
  await user.click(await confirmIn(t('organisation.team.end.confirm')));

  store.choose(ufer.id);
  await screen.findByText(t('organisation.team.members.empty'));
  answer(problem(409, 'organisation.last-manager', 'x'));

  // The switch closed the confirmation already; whatever the answer would show is shown once end has been told
  // (the toast) and the lists it reads again have come back.
  await handled(queryClient, () => expect(notify.failure).toHaveBeenCalled());
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  expect(within(membersSection()).queryByRole('alert')).not.toBeInTheDocument();
  expect(screen.queryByText(t('organisation.team.errors.lastManager'))).not.toBeInTheDocument();
});

test('a revoke refused only after the page switched to another pharmacy is not shown there', async () => {
  let answer: (response: Response) => void = () => undefined;
  host.revoke = () => new Promise<Response>((resolve) => (answer = resolve));
  const { user, store, queryClient } = await renderOnMarkt();
  await user.click(await revokeButton(ben));
  await user.click(await confirmIn(t('organisation.team.revoke.confirm')));

  store.choose(ufer.id);
  await screen.findByText(t('organisation.team.invitations.empty'));
  answer(problem(404, 'organisation.no-open-invitation', 'x'));

  // The switch closed the confirmation already; whatever the answer would show is shown once revoke has been told
  // (the toast) and the lists it reads again have come back.
  await handled(queryClient, () => expect(notify.failure).toHaveBeenCalled());
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
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
  let asked = false;
  host.invite = () => {
    asked = true;
    return new Promise<Response>((resolve) => (answer = resolve));
  };
  const { user, store, queryClient } = await renderOnMarkt();
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
  // The form validates for a few milliseconds before it sends (vee-validate's debounce): answer once it has.
  await vi.waitFor(() => expect(asked).toBe(true));
  answer(problem(409, 'organisation.already-member-or-invited', 'x'));

  // The switch closed the dialog already; whatever the answer would show is shown once it has been told (the toast)
  // and the lists it reads again have come back.
  await handled(queryClient, () => expect(notify.failure).toHaveBeenCalled());
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  expect(screen.queryByText(t('organisation.team.errors.alreadyMemberOrInvited'))).not.toBeInTheDocument();
  expect(uferWrites()).toEqual([]);
});

test('a change refused only after the page switched to another pharmacy is not shown there', async () => {
  let answer: (response: Response) => void = () => undefined;
  let asked = false;
  host.change = () => {
    asked = true;
    return new Promise<Response>((resolve) => (answer = resolve));
  };
  const { user, store, queryClient } = await renderOnMarkt();
  await user.click(await changeButton(anna));
  const dialog = within(await screen.findByRole('dialog', { name: t('organisation.team.change.title') }));
  await user.click(dialog.getByRole('button', { name: t('organisation.team.change.submit') }));

  store.choose(ufer.id);
  await showingUfer();
  // The form validates for a few milliseconds before it sends (vee-validate's debounce): answer once it has.
  await vi.waitFor(() => expect(asked).toBe(true));
  answer(problem(409, 'organisation.last-manager', 'x'));

  // The switch closed the dialog already; whatever the answer would show is shown once it has been told (the toast)
  // and the lists it reads again have come back.
  await handled(queryClient, () => expect(notify.failure).toHaveBeenCalled());
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
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

// A dialog closed by a switch and opened again is a new question: the answer to what the first opening sent never
// closes it, never shows in it and never keeps it from sending.

/** Fills the open invite dialog with an address and a job function and sends it. */
async function sendInvitation(user: User, email: string) {
  const dialog = within(await screen.findByRole('dialog', { name: t('organisation.team.invite.title') }));
  await user.click(dialog.getByRole('textbox', { name: t('organisation.team.invite.email') }));
  await user.paste(email);
  await user.selectOptions(
    dialog.getByRole('combobox', { name: t('organisation.team.invite.jobFunction') }),
    t('glossary.jobFunction.Pta'),
  );
  await user.click(dialog.getByRole('button', { name: t('organisation.team.invite.submit') }));
}

/** A host answer the test holds, and whether the page has asked for it yet. */
function held() {
  const hold = { asked: false, answer: (response: Response): void => void response };
  const ask = () => {
    hold.asked = true;
    return new Promise<Response>((resolve) => (hold.answer = resolve));
  };
  return { hold, ask };
}

/** Until the held answer was told (notify) and everything it set off has been read again. */
async function handled(queryClient: QueryClient, told: () => void) {
  await vi.waitFor(() => {
    told();
    expect(queryClient.isMutating()).toBe(0);
    expect(queryClient.isFetching()).toBe(0);
  });
}

/** Switches to the Apotheke am Ufer, then back to the Apotheke am Markt, once it lists its members again. */
async function awayAndBack(store: ReturnType<typeof useSelectedPharmacyStore>) {
  store.choose(ufer.id);
  await showingUfer();
  store.choose(markt.id);
  expect(await screen.findByText(t('organisation.team.intro', { pharmacy: markt.name }))).toBeInTheDocument();
  await rowOf(anna.name);
}

test('an invitation sent before a switch, accepted after it, leaves the invite dialog opened since open and empty', async () => {
  const { hold, ask } = held();
  host.invite = ask;
  const { user, store, queryClient } = await renderOnMarkt();
  await waitFor(() => expect(inviteButton()).toBeEnabled());
  await user.click(inviteButton());
  await sendInvitation(user, 'dora@example.com');

  store.choose(ufer.id);
  await showingUfer();
  await waitFor(() => expect(inviteButton()).toBeEnabled());
  await user.click(inviteButton());
  await screen.findByRole('dialog', { name: t('organisation.team.invite.title') });
  // The form validates for a few milliseconds before it sends (vee-validate's debounce): answer once it has.
  await vi.waitFor(() => expect(hold.asked).toBe(true));
  hold.answer(Response.json('0198c1a0-0000-7000-8000-0000000000b9', { status: 201 }));
  await handled(queryClient, () => expect(notify.success).toHaveBeenCalled());

  const dialog = within(screen.getByRole('dialog', { name: t('organisation.team.invite.title') }));
  expect(dialog.getByRole('textbox', { name: t('organisation.team.invite.email') })).toHaveValue('');
  expect(dialog.queryByRole('alert')).not.toBeInTheDocument();
});

test('an invitation sent before a switch, refused after it, shows nothing in the invite dialog opened since', async () => {
  const { hold, ask } = held();
  host.invite = ask;
  const { user, store, queryClient } = await renderOnMarkt();
  await waitFor(() => expect(inviteButton()).toBeEnabled());
  await user.click(inviteButton());
  await sendInvitation(user, 'dora@example.com');

  store.choose(ufer.id);
  await showingUfer();
  await waitFor(() => expect(inviteButton()).toBeEnabled());
  await user.click(inviteButton());
  await screen.findByRole('dialog', { name: t('organisation.team.invite.title') });
  // The form validates for a few milliseconds before it sends (vee-validate's debounce): answer once it has.
  await vi.waitFor(() => expect(hold.asked).toBe(true));
  hold.answer(problem(409, 'organisation.already-member-or-invited', 'x'));
  await handled(queryClient, () => expect(notify.failure).toHaveBeenCalled());

  const dialog = within(screen.getByRole('dialog', { name: t('organisation.team.invite.title') }));
  expect(dialog.queryByRole('alert')).not.toBeInTheDocument();
  expect(dialog.getByRole('textbox', { name: t('organisation.team.invite.email') })).not.toHaveAttribute(
    'aria-invalid',
  );
  expect(screen.queryByText(t('organisation.team.errors.alreadyMemberOrInvited'))).not.toBeInTheDocument();
});

test('an invitation still on its way from one pharmacy does not keep the invite dialog of another from sending', async () => {
  const { hold, ask } = held();
  host.invite = ask;
  const answeredByMarkt = fetch.getMockImplementation();
  fetch.mockImplementation(async (input, init) => {
    const url = new URL(String(input), 'http://localhost');
    if (init?.method === 'POST' && url.pathname === `/api/organisation/pharmacies/${ufer.id}/invitations`) {
      return Response.json('0198c1a0-0000-7000-8000-0000000000ba', { status: 201 });
    }
    return answeredByMarkt === undefined ? new Response('', { status: 404 }) : answeredByMarkt(input, init);
  });
  const { user, store } = await renderOnMarkt();
  await waitFor(() => expect(inviteButton()).toBeEnabled());
  await user.click(inviteButton());
  await sendInvitation(user, 'dora@example.com');
  await vi.waitFor(() => expect(hold.asked).toBe(true));

  store.choose(ufer.id);
  await showingUfer();
  await waitFor(() => expect(inviteButton()).toBeEnabled());
  await user.click(inviteButton());
  await sendInvitation(user, 'emil@example.com');

  await vi.waitFor(() =>
    expect(uferWrites()).toContainEqual(
      expect.objectContaining({
        method: 'POST',
        path: `/api/organisation/pharmacies/${ufer.id}/invitations`,
        body: expect.objectContaining({ email: 'emil@example.com' }) as unknown,
      }),
    ),
  );
  hold.answer(problem(409, 'organisation.already-member-or-invited', 'x'));
});

test('a change sent before switching away and back, refused after it, shows nothing in the change dialog opened since', async () => {
  const { hold, ask } = held();
  host.change = ask;
  const { user, store, queryClient } = await renderOnMarkt();
  await user.click(await changeButton(anna));
  const first = within(await screen.findByRole('dialog', { name: t('organisation.team.change.title') }));
  await user.click(first.getByRole('button', { name: t('organisation.team.change.submit') }));
  // The form validates for a few milliseconds before it sends (vee-validate's debounce): wait until it has.
  await vi.waitFor(() => expect(hold.asked).toBe(true));

  await awayAndBack(store);
  await user.click(await changeButton(anna));
  await screen.findByRole('dialog', { name: t('organisation.team.change.title') });
  hold.answer(problem(409, 'organisation.last-manager', 'x'));
  await handled(queryClient, () => expect(notify.failure).toHaveBeenCalled());

  const dialog = within(screen.getByRole('dialog', { name: t('organisation.team.change.title') }));
  expect(dialog.queryByRole('alert')).not.toBeInTheDocument();
  expect(screen.queryByText(t('organisation.team.errors.lastManager'))).not.toBeInTheDocument();
});

test('a change sent before switching away and back, made after it, leaves the change dialog opened since open', async () => {
  const { hold, ask } = held();
  host.change = ask;
  const { user, store, queryClient } = await renderOnMarkt();
  await user.click(await changeButton(anna));
  const first = within(await screen.findByRole('dialog', { name: t('organisation.team.change.title') }));
  await user.click(first.getByRole('button', { name: t('organisation.team.change.submit') }));
  // The form validates for a few milliseconds before it sends (vee-validate's debounce): wait until it has.
  await vi.waitFor(() => expect(hold.asked).toBe(true));

  await awayAndBack(store);
  await user.click(await changeButton(anna));
  await screen.findByRole('dialog', { name: t('organisation.team.change.title') });
  hold.answer(new Response(null, { status: 200 }));
  await handled(queryClient, () => expect(notify.success).toHaveBeenCalled());

  const dialog = within(screen.getByRole('dialog', { name: t('organisation.team.change.title') }));
  expect(dialog.queryByRole('alert')).not.toBeInTheDocument();
});

test('an end confirmed before switching away and back, refused after it, leaves the confirmation asked since open', async () => {
  const { hold, ask } = held();
  host.end = ask;
  const { user, store, queryClient } = await renderOnMarkt();
  await user.click(await endButton(anna));
  await user.click(await confirmIn(t('organisation.team.end.confirm')));
  await vi.waitFor(() => expect(hold.asked).toBe(true));

  await awayAndBack(store);
  await user.click(await endButton(anna));
  await screen.findByRole('dialog', { name: t('organisation.team.end.title', { name: anna.name }) });
  hold.answer(problem(404, 'organisation.no-active-membership', 'x'));
  await handled(queryClient, () => expect(notify.failure).toHaveBeenCalled());

  expect(
    screen.getByRole('dialog', { name: t('organisation.team.end.title', { name: anna.name }) }),
  ).toBeInTheDocument();
});

test('an end confirmation asked again after switching away and back is not busy with the end still on its way', async () => {
  const { hold, ask } = held();
  host.end = ask;
  const { user, store } = await renderOnMarkt();
  await user.click(await endButton(anna));
  await user.click(await confirmIn(t('organisation.team.end.confirm')));
  await vi.waitFor(() => expect(hold.asked).toBe(true));

  await awayAndBack(store);
  await user.click(await endButton(anna));

  expect(await confirmIn(t('organisation.team.end.confirm'))).toBeEnabled();
  expect(await confirmIn(t('organisation.team.cancel'))).toBeEnabled();
  hold.answer(problem(404, 'organisation.no-active-membership', 'x'));
});

test('a revoke confirmation asked again after switching away and back is not busy with the revoke still on its way', async () => {
  const { hold, ask } = held();
  host.revoke = ask;
  const { user, store } = await renderOnMarkt();
  await user.click(await revokeButton(ben));
  await user.click(await confirmIn(t('organisation.team.revoke.confirm')));
  await vi.waitFor(() => expect(hold.asked).toBe(true));

  await awayAndBack(store);
  await user.click(await revokeButton(ben));

  expect(await confirmIn(t('organisation.team.revoke.confirm'))).toBeEnabled();
  expect(await confirmIn(t('organisation.team.cancel'))).toBeEnabled();
  hold.answer(problem(404, 'organisation.no-open-invitation', 'x'));
});
