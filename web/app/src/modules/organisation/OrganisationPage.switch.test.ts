import { cleanup, screen, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { createPinia } from 'pinia';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import type { MyPharmacyDto } from '@/api/generated';
import { t } from '@/i18n';
import { useSelectedPharmacyStore } from '@/stores/selectedPharmacy';
import {
  answering,
  ben,
  endButton,
  type Fetch,
  fixClock,
  invitationsSection,
  markt,
  membersSection,
  problem,
  renderTeamPage,
  revokeButton,
  type TeamHost,
  teamHost,
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
  const message = t('errors.organisation.last-manager');
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
  const message = t('errors.organisation.no-open-invitation');
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
  expect(within(membersSection()).queryByRole('alert')).not.toBeInTheDocument();
  expect(screen.queryByText(t('errors.organisation.last-manager'))).not.toBeInTheDocument();
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
  expect(within(invitationsSection()).queryByRole('alert')).not.toBeInTheDocument();
  expect(screen.queryByText(t('errors.organisation.no-open-invitation'))).not.toBeInTheDocument();
});
