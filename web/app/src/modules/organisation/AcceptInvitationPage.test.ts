import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { cleanup, render, screen, waitFor, within } from '@testing-library/vue';
import { createPinia, type Pinia } from 'pinia';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import { defineComponent, h, type VNode } from 'vue';
import { createMemoryHistory, type RouteLocationNormalizedLoaded, type Router, RouterView } from 'vue-router';
import type { MyPharmacyDto } from '@/api/generated';
import { setLanguage, t } from '@/i18n';
import { createAppRouter } from '@/router';
import { useSelectedPharmacyStore } from '@/stores/selectedPharmacy';

const notify = vi.hoisted(() => ({ success: vi.fn(), failure: vi.fn() }));
vi.mock('@/lib/notify', () => ({ notify }));

// The invitation mail's link, /invitations/accept#token=<token>: its route keeps the token for this tab
// (sessionStorage, invitation-token) and leaves the address without it; the page takes it out of storage in one read
// and posts it once. Who accepts comes from the access token, never from the page.
const token = 'k7Yq2_Lm-9XwPz4RtV8sNc1HjD6bFgUe0OiA5yQrTmW';
const key = 'invitation-token';
const link = `/invitations/accept#token=${token}`;

/** The pharmacy the invitation is to, as the host answers POST /organisation/invitations/accept: its id. */
const pharmacyId = '0198c1a0-0000-7000-8000-0000000000a7';
const joined: MyPharmacyDto = {
  id: pharmacyId,
  name: 'Apotheke am Markt',
  membershipId: '0198c1a0-0000-7000-8000-0000000000b7',
  role: 'Employee',
  jobFunction: 'Pta',
  canManageMembers: false,
  canAssignCourses: false,
};
const membershipsOf = (items: MyPharmacyDto[]) =>
  Response.json({ items, totalCount: items.length, page: 1, pageSize: 100 });

// The host as the API client reaches it: POST /organisation/invitations/accept answers what the test says (200 with the
// pharmacy's id unless told otherwise); once it has accepted, GET /organisation/pharmacies/mine holds the membership,
// and GET /organisation/pharmacies/{id} answers the pharmacy. Anything else waits for an answer that never comes.
const host = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>();
vi.stubGlobal('fetch', host);

let acceptance: () => Promise<Response>;
let membershipsAfterAcceptance: MyPharmacyDto[];
let pharmacy: () => Promise<Response>;

beforeEach(() => {
  sessionStorage.clear();
  localStorage.clear();
  acceptance = async () => Response.json(pharmacyId);
  membershipsAfterAcceptance = [joined];
  pharmacy = async () => Response.json(joined);
  let accepted = false;
  host.mockImplementation(async (input, init) => {
    const url = String(input);
    if (init?.method === 'POST' && url.endsWith('/api/organisation/invitations/accept')) {
      const response = await acceptance();
      accepted ||= response.ok;
      return response;
    }
    if (url.includes('/api/organisation/pharmacies/mine')) {
      return membershipsOf(accepted ? membershipsAfterAcceptance : []);
    }
    if (url.endsWith(`/api/organisation/pharmacies/${pharmacyId}`)) {
      return pharmacy();
    }
    return new Promise<Response>(() => undefined);
  });
  notify.success.mockReset();
  notify.failure.mockReset();
});
afterEach(() => {
  host.mockReset();
  sessionStorage.clear();
  localStorage.clear();
});
// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(cleanup);

/** What the page posted to accept, as the host received it. */
const acceptances = () =>
  host.mock.calls
    .filter(
      ([input, init]) => init?.method === 'POST' && String(input).endsWith('/api/organisation/invitations/accept'),
    )
    .map(([, init]) => JSON.parse(String(init?.body)) as unknown);

/** Every value kept in localStorage, which outlives the tab and every other tab shares. */
const localValues = () =>
  Array.from({ length: localStorage.length }, (_, index) => localStorage.key(index)).map((name) =>
    name === null ? '' : `${name}=${localStorage.getItem(name) ?? ''}`,
  );

// The page as the router shows it at its address, alone: the page the user is sent on to (Meine Schulungen, the start
// page) is not rendered, only its address read.
const AtTheLink = defineComponent({
  setup() {
    return () =>
      h(RouterView, null, {
        default: ({ Component, route }: { Component: VNode | undefined; route: RouteLocationNormalizedLoaded }) =>
          route.path === '/invitations/accept' ? Component : null,
      });
  },
});

/**
 * The app's router at an address (the link with its token, unless a test says otherwise), and the page rendered at it
 * with a query client that does not retry, so a failure shows at once.
 */
async function openPage(address: string = link, options: { pinia?: Pinia; router?: Router } = {}) {
  const router = options.router ?? createAppRouter(createMemoryHistory());
  await router.push(address);
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  const pinia = options.pinia ?? createPinia();
  const view = render(AtTheLink, { global: { plugins: [router, pinia, [VueQueryPlugin, { queryClient }]] } });
  return { router, pinia, view };
}

/** A promise the test settles when it says, for an answer that is still on its way. */
function deferred() {
  let settle: (response: Response) => void = () => undefined;
  const promise = new Promise<Response>((resolve) => {
    settle = resolve;
  });
  return { promise, settle };
}

test('with a kept token the page accepts it once and the employee lands on Meine Schulungen with the pharmacy chosen', async () => {
  const { router, pinia } = await openPage();

  await waitFor(() => expect(router.currentRoute.value.path).toBe('/learning'));
  expect(acceptances()).toEqual([{ token }]);
  expect(useSelectedPharmacyStore(pinia).pharmacyId).toBe(pharmacyId);
  expect(notify.success).toHaveBeenCalledTimes(1);
  expect(notify.success).toHaveBeenCalledWith(t('organisation.accept.accepted', { name: 'Apotheke am Markt' }));
  expect(notify.success).toHaveBeenCalledWith('Sie sind jetzt Mitglied der Apotheke Apotheke am Markt.');
  expect(notify.failure).not.toHaveBeenCalled();
});

// The address is replaced, not pushed: Back does not lead to the page again, which would find no token.
test('the page replaces its address with Meine Schulungen, so Back does not return to it', async () => {
  const { router } = await openPage();

  await waitFor(() => expect(router.currentRoute.value.path).toBe('/learning'));
  const entry = router.options.history.state as { back: unknown; current: unknown };
  expect(entry.current).toBe('/learning');
  expect(entry.back).toBeNull();
});

test('the pharmacy is opened only once the memberships read again hold it', async () => {
  const { router } = await openPage();

  await waitFor(() => expect(router.currentRoute.value.path).toBe('/learning'));
  const calls = host.mock.calls.map(([input, init]) => `${init?.method ?? 'GET'} ${String(input)}`);
  const accepted = calls.findIndex((call) => call.endsWith('/api/organisation/invitations/accept'));
  const reread = calls.findIndex(
    (call, index) => index > accepted && call.includes('/api/organisation/pharmacies/mine'),
  );
  expect(accepted).toBeGreaterThanOrEqual(0);
  expect(reread).toBeGreaterThan(accepted);
});

test('the token is taken out of storage once it is read, and never kept in localStorage', async () => {
  const { router } = await openPage();

  expect(sessionStorage.getItem(key)).toBeNull();
  await waitFor(() => expect(router.currentRoute.value.path).toBe('/learning'));
  expect(sessionStorage.getItem(key)).toBeNull();
  expect(localValues().join('\n')).not.toContain(token);
});

test('accepted but not among the memberships read again, the page leads home and says so', async () => {
  membershipsAfterAcceptance = [];
  const { router, pinia } = await openPage();

  await waitFor(() => expect(router.currentRoute.value.path).toBe('/'));
  expect(acceptances()).toEqual([{ token }]);
  expect(useSelectedPharmacyStore(pinia).pharmacyId).toBeNull();
  expect(notify.success).toHaveBeenCalledWith(t('organisation.accept.acceptedNotOpened'));
  expect(notify.success).toHaveBeenCalledWith(
    'Sie haben die Einladung angenommen, die Apotheke konnte aber nicht geöffnet werden. Wählen Sie sie unter „Apotheke“ in der Seitenleiste.',
  );
});

test("the pharmacy's name failing to load still lands on Meine Schulungen, without the name", async () => {
  pharmacy = async () => new Response('', { status: 500 });
  const { router, pinia } = await openPage();

  await waitFor(() => expect(router.currentRoute.value.path).toBe('/learning'));
  expect(useSelectedPharmacyStore(pinia).pharmacyId).toBe(pharmacyId);
  expect(notify.success).toHaveBeenCalledWith(t('organisation.accept.acceptedUnnamed'));
  expect(notify.success).toHaveBeenCalledWith('Sie haben die Einladung angenommen.');
});

test('an answer that is not a pharmacy id is no pharmacy to open: the page leads home and says it was accepted', async () => {
  acceptance = async () => Response.json('not-an-id');
  const { router, pinia } = await openPage();

  await waitFor(() => expect(router.currentRoute.value.path).toBe('/'));
  expect(useSelectedPharmacyStore(pinia).pharmacyId).toBeNull();
  expect(notify.success).toHaveBeenCalledWith(t('organisation.accept.acceptedNotOpened'));
});

test('while the invitation is accepted the page is busy, a skeleton, and says so to a screen reader', async () => {
  const answer = deferred();
  acceptance = () => answer.promise;
  await openPage();

  const main = await screen.findByRole('main');
  await waitFor(() => expect(main).toHaveAttribute('aria-busy', 'true'));
  expect(within(main).getByText(t('organisation.accept.accepting'))).toHaveClass('sr-only');
  expect(within(main).getByText('Die Einladung wird angenommen …')).toBeInTheDocument();
  expect(main.querySelector('[data-slot="skeleton"]')).not.toBeNull();
  expect(document.body.textContent).not.toContain(token);

  answer.settle(Response.json(pharmacyId));
});

test('without a kept token nothing is posted and the page says to open the link again', async () => {
  await openPage('/invitations/accept');

  const main = within(await screen.findByRole('main'));
  const heading = main.getByRole('heading', { level: 1, name: t('organisation.accept.missing.title') });
  expect(heading).toHaveClass('text-title');
  expect(heading).toHaveTextContent('Kein gültiger Einladungslink');
  expect(main.getByText(t('organisation.accept.missing.text'))).toBeInTheDocument();
  expect(main.getByRole('link', { name: t('shell.gate.goHome') })).toHaveAttribute('href', '/');
  expect(acceptances()).toEqual([]);
});

// The link's token is gone once the guard has read the address: a token left in the fragment would be the page's to
// show, and it never is.
test('a malformed token in the link is not posted, and the page says to open the link again', async () => {
  await openPage(`/invitations/accept#token=${token.slice(5)}`);

  expect(
    await screen.findByRole('heading', { level: 1, name: t('organisation.accept.missing.title') }),
  ).toBeInTheDocument();
  expect(acceptances()).toEqual([]);
  expect(document.body.textContent).not.toContain(token.slice(5));
});

// Storage is untrusted input: the page checks what it takes as the guard checked it.
test('a kept value that is not a token is not posted, and is removed', async () => {
  sessionStorage.setItem(key, 'not-a-token');

  await openPage('/invitations/accept');

  expect(
    await screen.findByRole('heading', { level: 1, name: t('organisation.accept.missing.title') }),
  ).toBeInTheDocument();
  expect(acceptances()).toEqual([]);
  expect(sessionStorage.getItem(key)).toBeNull();
});

// Back from the identity provider: the address has no fragment, the token kept before the sign-in is in storage.
test('a token kept before the sign-in is accepted on the way back', async () => {
  sessionStorage.setItem(key, token);

  const { router } = await openPage('/invitations/accept?code=abc&state=def');

  await waitFor(() => expect(router.currentRoute.value.path).toBe('/learning'));
  expect(acceptances()).toEqual([{ token }]);
});

test('the token is posted once while the page renders again', async () => {
  const answer = deferred();
  acceptance = () => answer.promise;
  const { router } = await openPage();
  await waitFor(() => expect(acceptances()).toHaveLength(1));

  // A switch of the language renders every text of the page again.
  setLanguage('en-GB', { remember: false });
  expect(await screen.findByText('Accepting the invitation …')).toBeInTheDocument();
  answer.settle(Response.json(pharmacyId));

  await waitFor(() => expect(router.currentRoute.value.path).toBe('/learning'));
  expect(acceptances()).toEqual([{ token }]);
});

// Mounted twice, as a remount or a second view of the same route would: the token is in storage for one read only.
test('the token is posted once when the page is mounted twice', async () => {
  const answer = deferred();
  acceptance = () => answer.promise;
  const router = createAppRouter(createMemoryHistory());
  const pinia = createPinia();
  await openPage(link, { router, pinia });
  await openPage('/invitations/accept', { router, pinia });
  await waitFor(() => expect(acceptances()).toHaveLength(1));

  answer.settle(Response.json(pharmacyId));

  await waitFor(() => expect(router.currentRoute.value.path).toBe('/learning'));
  expect(acceptances()).toEqual([{ token }]);
});

test('unmounted while the invitation is accepted and mounted again, the page posts nothing more', async () => {
  const answer = deferred();
  acceptance = () => answer.promise;
  const { router, pinia, view } = await openPage();
  await waitFor(() => expect(acceptances()).toHaveLength(1));

  view.unmount();
  await openPage('/invitations/accept', { router, pinia });

  expect(
    await screen.findByRole('heading', { level: 1, name: t('organisation.accept.missing.title') }),
  ).toBeInTheDocument();
  expect(acceptances()).toEqual([{ token }]);
  answer.settle(Response.json(pharmacyId));
});

test('a second visit without the link finds no token and says to open the link again', async () => {
  const { router, pinia } = await openPage();
  await waitFor(() => expect(router.currentRoute.value.path).toBe('/learning'));
  cleanup();

  await openPage('/invitations/accept', { router, pinia });

  expect(
    await screen.findByRole('heading', { level: 1, name: t('organisation.accept.missing.title') }),
  ).toBeInTheDocument();
  expect(acceptances()).toEqual([{ token }]);
});

test('without a kept token the page says to open the link again in English', async () => {
  setLanguage('en-GB', { remember: false });

  await openPage('/invitations/accept');

  const main = within(await screen.findByRole('main'));
  expect(main.getByRole('heading', { level: 1, name: 'No valid invitation link' })).toBeInTheDocument();
  expect(
    main.getByText(
      'Open the link in your invitation e-mail again, in full and in this browser. As you are signed in now, the invitation will then be accepted straight away.',
    ),
  ).toBeInTheDocument();
});
