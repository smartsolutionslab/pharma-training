import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { cleanup, render, screen, waitFor, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { createPinia, type Pinia } from 'pinia';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import { defineComponent, h, type VNode } from 'vue';
import {
  createMemoryHistory,
  createWebHistory,
  type RouteLocationNormalizedLoaded,
  type Router,
  RouterView,
} from 'vue-router';
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
  vi.restoreAllMocks();
  vi.useRealTimers();
  sessionStorage.clear();
  localStorage.clear();
  window.history.replaceState({}, '', '/');
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
  // Memory history keeps no back/current state as web history does; what it is asked to do shows replace over push.
  const history = createMemoryHistory();
  const push = vi.spyOn(history, 'push');
  const replace = vi.spyOn(history, 'replace');
  const router = createAppRouter(history);
  await openPage(link, { router });
  push.mockClear();
  replace.mockClear();

  await waitFor(() => expect(router.currentRoute.value.path).toBe('/learning'));
  expect(replace.mock.calls.map(([to]) => to)).toEqual(['/learning']);
  expect(push).not.toHaveBeenCalled();
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
  // The link opened before the sign-in, in this tab: its guard kept the token, with the time it was kept.
  await createAppRouter(createMemoryHistory()).push(link);

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

/** A refusal as the host sends it: problem details with the module's code, in the host's words (German and English). */
const problem = (status: number, code: string | undefined, detail: string) =>
  Response.json(
    {
      type: 'https://tools.ietf.org/html/rfc9110',
      title: detail,
      status,
      detail,
      ...(code === undefined ? {} : { code }),
    },
    { status, headers: { 'Content-Type': 'application/problem+json' } },
  );

// Each refusal the host answers with, and the state the page shows for it: its title and text from the catalog, never
// the host's words, and the way on. None of them is worth posting the same token again.
const refusals = [
  {
    status: 400,
    code: 'organisation.invalid-invitation-token',
    state: 'missing',
    way: { name: 'shell.gate.goHome', to: '/' },
  },
  {
    status: 404,
    code: 'organisation.invitation-not-found',
    state: 'notFound',
    way: { name: 'shell.gate.goHome', to: '/' },
  },
  {
    status: 410,
    code: 'organisation.invitation-expired',
    state: 'expired',
    way: { name: 'shell.gate.goHome', to: '/' },
  },
  {
    status: 403,
    code: 'organisation.invitation-for-another-address',
    state: 'anotherAddress',
    way: { name: 'shell.gate.goHome', to: '/' },
  },
  {
    status: 403,
    code: 'organisation.email-not-verified',
    state: 'emailNotVerified',
    way: { name: 'shell.gate.goHome', to: '/' },
  },
  {
    status: 403,
    code: 'organisation.identity-incomplete',
    state: 'identityIncomplete',
    way: { name: 'shell.gate.goHome', to: '/' },
  },
  {
    status: 409,
    code: 'organisation.person-already-member',
    state: 'alreadyMember',
    way: { name: 'organisation.accept.goToCourses', to: '/learning' },
  },
] as const;

const hostWords = 'Die Antwort des Hosts, wie er sie sendet.';

test.each(refusals)(
  'a $status $code shows the $state state, its own text and the way on, and stays on the page',
  async ({ status, code, state, way }) => {
    acceptance = async () => problem(status, code, hostWords);
    const { router, pinia } = await openPage();

    const main = within(await screen.findByRole('main'));
    const heading = await main.findByRole('heading', { level: 1, name: t(`organisation.accept.${state}.title`) });
    expect(heading).toHaveClass('text-title');
    expect(main.getByText(t(`organisation.accept.${state}.text`))).toBeInTheDocument();
    expect(main.getByRole('link', { name: t(way.name) })).toHaveAttribute('href', way.to);
    expect(main.queryByRole('button', { name: t('shell.retry') })).not.toBeInTheDocument();
    expect(main.queryByText(hostWords)).not.toBeInTheDocument();
    expect(document.body.textContent).not.toContain(token);
    expect(acceptances()).toEqual([{ token }]);
    expect(router.currentRoute.value.path).toBe('/invitations/accept');
    expect(useSelectedPharmacyStore(pinia).pharmacyId).toBeNull();
    expect(notify.success).not.toHaveBeenCalled();
  },
);

test('an expired invitation says so in German, and to ask for it again', async () => {
  acceptance = async () => problem(410, 'organisation.invitation-expired', 'Invitation expired.');
  await openPage();

  const main = within(await screen.findByRole('main'));
  expect(await main.findByRole('heading', { level: 1, name: 'Einladung abgelaufen' })).toBeInTheDocument();
  expect(
    main.getByText('Diese Einladung ist abgelaufen. Bitten Sie Ihre Apothekenleitung, sie erneut zu senden.'),
  ).toBeInTheDocument();
  expect(main.getByRole('link', { name: 'Zur Startseite' })).toHaveAttribute('href', '/');
});

// A forwarded link, opened by someone signed in with another address than the invited one.
test('an invitation for another address says so in German', async () => {
  acceptance = async () => problem(403, 'organisation.invitation-for-another-address', hostWords);
  await openPage();

  expect(
    await screen.findByRole('heading', { level: 1, name: 'Einladung für eine andere Adresse' }),
  ).toBeInTheDocument();
  expect(
    screen.getByText(
      'Diese Einladung gilt für eine andere E-Mail-Adresse als die Ihres Kontos. Wurde sie an Sie weitergeleitet, bitten Sie die Apothekenleitung, Ihre eigene Adresse einzuladen. Ist es Ihre Einladung, melden Sie sich mit der eingeladenen Adresse an und öffnen Sie den Link erneut.',
    ),
  ).toBeInTheDocument();
});

test('a member already leads to Meine Schulungen, in German', async () => {
  acceptance = async () => problem(409, 'organisation.person-already-member', hostWords);
  await openPage();

  expect(await screen.findByRole('heading', { level: 1, name: 'Bereits Mitglied' })).toBeInTheDocument();
  expect(screen.getByRole('link', { name: 'Zu „Meine Schulungen“' })).toHaveAttribute('href', '/learning');
});

// The two 403s and the two 409s share a status: the page tells them apart by the module's code.
test('the two refusals of one status are told apart by their code', async () => {
  acceptance = async () => problem(403, 'organisation.email-not-verified', hostWords);
  await openPage();
  expect(await screen.findByRole('heading', { level: 1, name: 'E-Mail-Adresse nicht bestätigt' })).toBeInTheDocument();
  expect(screen.queryByRole('heading', { name: 'Einladung für eine andere Adresse' })).not.toBeInTheDocument();
  cleanup();

  acceptance = async () => problem(403, 'organisation.identity-incomplete', hostWords);
  await openPage();
  expect(await screen.findByRole('heading', { level: 1, name: 'Konto unvollständig' })).toBeInTheDocument();
});

// Answers worth trying again: the page keeps the token it took in memory, and Retry posts the same one.
const retryable = [
  {
    answer: 'a 429',
    refusal: async () => problem(429, undefined, 'Too Many Requests'),
    state: 'tooMany',
  },
  {
    answer: 'a 409 pharmacy-changed',
    refusal: async () => problem(409, 'organisation.pharmacy-changed', 'Die Apotheke wurde gerade geändert.'),
    state: 'failed',
  },
  { answer: 'a 503', refusal: async () => problem(503, undefined, 'Service Unavailable'), state: 'failed' },
  { answer: 'a 500 without a body', refusal: async () => new Response('', { status: 500 }), state: 'failed' },
  {
    answer: 'a network failure',
    refusal: () => Promise.reject(new TypeError('Failed to fetch')),
    state: 'failed',
  },
] as const;

test.each(retryable)(
  '$answer shows the $state state with Retry, which posts the same token again and lands on Meine Schulungen',
  async ({ refusal, state }) => {
    const user = userEvent.setup();
    acceptance = refusal;
    const { router, pinia } = await openPage();

    const main = within(await screen.findByRole('main'));
    expect(await main.findByRole('heading', { level: 1, name: t(`organisation.accept.${state}.title`) })).toHaveClass(
      'text-title',
    );
    expect(main.getByRole('link', { name: t('shell.gate.goHome') })).toHaveAttribute('href', '/');
    expect(acceptances()).toEqual([{ token }]);
    expect(sessionStorage.getItem(key)).toBeNull();

    acceptance = async () => Response.json(pharmacyId);
    await user.click(main.getByRole('button', { name: t('shell.retry') }));

    await waitFor(() => expect(router.currentRoute.value.path).toBe('/learning'));
    expect(acceptances()).toEqual([{ token }, { token }]);
    expect(useSelectedPharmacyStore(pinia).pharmacyId).toBe(pharmacyId);
  },
);

test('too many attempts says so in German', async () => {
  acceptance = async () => problem(429, undefined, 'Too Many Requests');
  await openPage();

  expect(await screen.findByRole('heading', { level: 1, name: 'Zu viele Versuche' })).toBeInTheDocument();
  expect(screen.getByText('Versuchen Sie es in einigen Minuten erneut.')).toBeInTheDocument();
  expect(screen.queryByText('Too Many Requests')).not.toBeInTheDocument();
});

test('a failure the host gives no words for shows the catalog text, in German', async () => {
  acceptance = async () => problem(503, undefined, 'Service Unavailable');
  await openPage();

  expect(await screen.findByRole('heading', { level: 1, name: 'Einladung nicht angenommen' })).toBeInTheDocument();
  expect(
    screen.getByText('Die Einladung konnte nicht angenommen werden. Versuchen Sie es erneut.'),
  ).toBeInTheDocument();
  expect(screen.queryByText('Service Unavailable')).not.toBeInTheDocument();
});

// A refusal of the module's the page has no text of its own for: the module's words as it sent them (ADR 0007).
test("a module's refusal the page does not know shows the module's words under the failed title", async () => {
  acceptance = async () =>
    problem(422, 'organisation.pharmacy-closed', 'Die Apotheke nimmt keine Mitglieder mehr auf.');
  await openPage();

  const main = within(await screen.findByRole('main'));
  expect(
    await main.findByRole('heading', { level: 1, name: t('organisation.accept.failed.title') }),
  ).toBeInTheDocument();
  expect(main.getByText('Die Apotheke nimmt keine Mitglieder mehr auf.')).toBeInTheDocument();
  expect(main.queryByText(t('organisation.accept.failed.text'))).not.toBeInTheDocument();
});

// Verification followed in another tab: back from the identity provider, but that tab's sessionStorage never held the
// token. The link is still valid, since nothing accepted it.
test('back from the sign-in in a tab that never held the token, the page says to open the link again', async () => {
  await openPage('/invitations/accept?code=abc&state=def');

  expect(await screen.findByRole('heading', { level: 1, name: 'Kein gültiger Einladungslink' })).toBeInTheDocument();
  expect(
    screen.getByText(
      'Öffnen Sie den Link aus Ihrer Einladungs-E-Mail noch einmal, vollständig und in diesem Browser. Da Sie jetzt angemeldet sind, wird die Einladung dann sofort angenommen.',
    ),
  ).toBeInTheDocument();
  expect(acceptances()).toEqual([]);
});

// Each state's title in English, as the catalog's en.ts holds it.
test.each([
  { status: 404, code: 'organisation.invitation-not-found', title: 'Invitation not found' },
  { status: 410, code: 'organisation.invitation-expired', title: 'Invitation expired' },
  { status: 403, code: 'organisation.invitation-for-another-address', title: 'Invitation for another address' },
  { status: 403, code: 'organisation.email-not-verified', title: 'E-mail address not verified' },
  { status: 403, code: 'organisation.identity-incomplete', title: 'Account incomplete' },
  { status: 409, code: 'organisation.person-already-member', title: 'Already a member' },
  { status: 400, code: 'organisation.invalid-invitation-token', title: 'No valid invitation link' },
  { status: 429, code: undefined, title: 'Too many attempts' },
  { status: 503, code: undefined, title: 'Invitation not accepted' },
])('in English a $status answer says $title', async ({ status, code, title }) => {
  setLanguage('en-GB', { remember: false });
  acceptance = async () => problem(status, code, hostWords);
  await openPage();

  expect(await screen.findByRole('heading', { level: 1, name: title })).toBeInTheDocument();
});

test('in English a member already is led to My courses', async () => {
  setLanguage('en-GB', { remember: false });
  acceptance = async () => problem(409, 'organisation.person-already-member', hostWords);
  await openPage();

  expect(await screen.findByRole('link', { name: 'Go to My courses' })).toHaveAttribute('href', '/learning');
  expect(
    screen.getByText('You are already a member of this pharmacy. Choose it under “Pharmacy” in the sidebar.'),
  ).toBeInTheDocument();
});

// The sidebar's switcher is “Pharmacy” in English, in curly quotes, wherever a text sends the user to it.
test('in English every text that sends the user to the switcher names it alike', () => {
  setLanguage('en-GB', { remember: false });

  for (const text of [
    t('organisation.accept.alreadyMember.text'),
    t('organisation.accept.acceptedNotOpened'),
    t('organisation.register.registeredNotOpened', { name: 'Apotheke am Markt' }),
  ]) {
    expect(text).toContain('under “Pharmacy” in the sidebar');
  }
});

// A second link opened in the same tab while the page is shown: the address changes only in its fragment, the route
// stays, and vue-router runs no beforeEnter for it. The token must still leave the address, and be the one accepted.
const second = 'Bq3x_Second-InvitationTokenOpenedInThisTab0';
const secondLink = `/invitations/accept#token=${second}`;

test('a second link opened while the page is shown leaves the address, and its token is accepted once', async () => {
  const { router, pinia } = await openPage('/invitations/accept');
  expect(
    await screen.findByRole('heading', { level: 1, name: t('organisation.accept.missing.title') }),
  ).toBeInTheDocument();

  await router.push(secondLink);

  expect(router.currentRoute.value.hash).toBe('');
  expect(router.currentRoute.value.fullPath).not.toContain(second);
  await waitFor(() => expect(router.currentRoute.value.path).toBe('/learning'));
  expect(acceptances()).toEqual([{ token: second }]);
  expect(sessionStorage.getItem(key)).toBeNull();
  expect(useSelectedPharmacyStore(pinia).pharmacyId).toBe(pharmacyId);
});

// The first link was forwarded to the wrong person; the right link, opened next in the same tab, is accepted.
test('a second link opened after a refusal is accepted, and the page shows its outcome', async () => {
  acceptance = async () => problem(403, 'organisation.invitation-for-another-address', hostWords);
  const { router } = await openPage();
  expect(
    await screen.findByRole('heading', { level: 1, name: t('organisation.accept.anotherAddress.title') }),
  ).toBeInTheDocument();
  acceptance = async () => Response.json(pharmacyId);

  await router.push(secondLink);

  expect(router.currentRoute.value.fullPath).not.toContain(second);
  await waitFor(() => expect(router.currentRoute.value.path).toBe('/learning'));
  expect(acceptances()).toEqual([{ token }, { token: second }]);
  expect(sessionStorage.getItem(key)).toBeNull();
});

test("a second link's refusal is shown in place of the state before it", async () => {
  const { router } = await openPage('/invitations/accept');
  expect(
    await screen.findByRole('heading', { level: 1, name: t('organisation.accept.missing.title') }),
  ).toBeInTheDocument();
  acceptance = async () => problem(410, 'organisation.invitation-expired', hostWords);

  await router.push(secondLink);

  expect(
    await screen.findByRole('heading', { level: 1, name: t('organisation.accept.expired.title') }),
  ).toBeInTheDocument();
  expect(router.currentRoute.value.fullPath).not.toContain(second);
  expect(acceptances()).toEqual([{ token: second }]);
});

test('a second link with a malformed token leaves the address and the page says to open the link again', async () => {
  acceptance = async () => problem(403, 'organisation.invitation-for-another-address', hostWords);
  const { router } = await openPage();
  expect(
    await screen.findByRole('heading', { level: 1, name: t('organisation.accept.anotherAddress.title') }),
  ).toBeInTheDocument();

  await router.push(`/invitations/accept#token=${second.slice(4)}`);

  expect(router.currentRoute.value.hash).toBe('');
  expect(router.currentRoute.value.fullPath).not.toContain(second.slice(4));
  expect(
    await screen.findByRole('heading', { level: 1, name: t('organisation.accept.missing.title') }),
  ).toBeInTheDocument();
  expect(acceptances()).toEqual([{ token }]);
  expect(sessionStorage.getItem(key)).toBeNull();
});

test("with the browser's history a second link leaves the address without an entry holding its token", async () => {
  window.history.replaceState({}, '', '/invitations/accept');
  const router = createAppRouter(createWebHistory());
  await openPage('/invitations/accept', { router });
  expect(
    await screen.findByRole('heading', { level: 1, name: t('organisation.accept.missing.title') }),
  ).toBeInTheDocument();
  const pushState = vi.spyOn(window.history, 'pushState');
  const replaceState = vi.spyOn(window.history, 'replaceState');

  await router.push(secondLink);

  expect(window.location.pathname).toBe('/invitations/accept');
  expect(window.location.hash).toBe('');
  expect(window.location.href).not.toContain(second);
  expect(pushState).not.toHaveBeenCalled();
  expect(replaceState.mock.calls.map(([, , url]) => String(url)).join('\n')).not.toContain(second);
  expect(JSON.stringify(window.history.state)).not.toContain(second);
  await waitFor(() => expect(router.currentRoute.value.path).toBe('/learning'));
  expect(acceptances()).toEqual([{ token: second }]);
});

// Retry is offered only where trying again can succeed: too many attempts, a lost concurrent save, the host
// unavailable, the network. A refusal of the request itself is final.
test("a framework's invalid-argument shows the failed state without Retry, with a way home", async () => {
  acceptance = async () => problem(400, 'invalid-argument', 'One or more validation errors occurred.');
  await openPage();

  const main = within(await screen.findByRole('main'));
  expect(
    await main.findByRole('heading', { level: 1, name: t('organisation.accept.failed.title') }),
  ).toBeInTheDocument();
  expect(main.queryByRole('button', { name: t('shell.retry') })).not.toBeInTheDocument();
  expect(main.getByRole('link', { name: t('shell.gate.goHome') })).toHaveAttribute('href', '/');
});

test("a module's refusal the page does not know offers no Retry, only a way home", async () => {
  acceptance = async () =>
    problem(422, 'organisation.pharmacy-closed', 'Die Apotheke nimmt keine Mitglieder mehr auf.');
  await openPage();

  const main = within(await screen.findByRole('main'));
  expect(await main.findByText('Die Apotheke nimmt keine Mitglieder mehr auf.')).toBeInTheDocument();
  expect(main.queryByRole('button', { name: t('shell.retry') })).not.toBeInTheDocument();
  expect(main.getByRole('link', { name: t('shell.gate.goHome') })).toHaveAttribute('href', '/');
});

// The first attempt may have been accepted although its answer never arrived: a Retry then finds the invitation used.
test('a Retry answered not found says the invitation may already be accepted, and leads to Meine Schulungen', async () => {
  const user = userEvent.setup();
  acceptance = () => Promise.reject(new TypeError('Failed to fetch'));
  const { router } = await openPage();
  const main = within(await screen.findByRole('main'));
  acceptance = async () => problem(404, 'organisation.invitation-not-found', hostWords);

  await user.click(await main.findByRole('button', { name: t('shell.retry') }));

  expect(await main.findByText(t('organisation.accept.retryNotFound'))).toBeInTheDocument();
  expect(
    main.getByText(
      'Die Einladung ist nicht mehr gültig. Wenn Sie sie eben schon angenommen haben, finden Sie die Apotheke unter „Meine Schulungen“.',
    ),
  ).toBeInTheDocument();
  expect(main.getByRole('heading', { level: 1, name: t('organisation.accept.failed.title') })).toBeInTheDocument();
  expect(main.getByRole('link', { name: t('organisation.accept.goToCourses') })).toHaveAttribute('href', '/learning');
  expect(main.getByRole('link', { name: t('shell.gate.goHome') })).toHaveAttribute('href', '/');
  expect(main.queryByRole('button', { name: t('shell.retry') })).not.toBeInTheDocument();
  expect(main.queryByText(hostWords)).not.toBeInTheDocument();
  expect(acceptances()).toEqual([{ token }, { token }]);
  expect(router.currentRoute.value.path).toBe('/invitations/accept');
});

test('in English a Retry answered not found says the invitation may already be accepted', () => {
  setLanguage('en-GB', { remember: false });

  expect(t('organisation.accept.retryNotFound')).toBe(
    'The invitation is no longer valid. If you have just accepted it, you will find the pharmacy under “My courses”.',
  );
});

// The page's heading takes focus whenever its state changes, so a screen reader announces the new state.
test('a refusal moves focus to the page heading', async () => {
  acceptance = async () => problem(410, 'organisation.invitation-expired', hostWords);
  await openPage();

  const heading = await screen.findByRole('heading', { level: 1, name: t('organisation.accept.expired.title') });
  expect(heading).toHaveAttribute('tabindex', '-1');
  await waitFor(() => expect(heading).toHaveFocus());
});

test('a failure moves focus to the page heading', async () => {
  acceptance = () => Promise.reject(new TypeError('Failed to fetch'));
  await openPage();

  const heading = await screen.findByRole('heading', { level: 1, name: t('organisation.accept.failed.title') });
  expect(heading).toHaveAttribute('tabindex', '-1');
  await waitFor(() => expect(heading).toHaveFocus());
});

test("pressing Retry moves focus to the heading while accepting, and to the next state's heading after", async () => {
  const user = userEvent.setup();
  acceptance = async () => problem(503, undefined, 'Service Unavailable');
  await openPage();
  const failed = await screen.findByRole('heading', { level: 1, name: t('organisation.accept.failed.title') });
  await waitFor(() => expect(failed).toHaveFocus());
  const answer = deferred();
  acceptance = () => answer.promise;

  await user.click(screen.getByRole('button', { name: t('shell.retry') }));

  const accepting = await screen.findByRole('heading', { level: 1, name: t('organisation.accept.title') });
  expect(accepting).toHaveAttribute('tabindex', '-1');
  await waitFor(() => expect(accepting).toHaveFocus());
  expect(document.activeElement).not.toBe(document.body);

  answer.settle(problem(429, undefined, 'Too Many Requests'));

  const tooMany = await screen.findByRole('heading', { level: 1, name: t('organisation.accept.tooMany.title') });
  await waitFor(() => expect(tooMany).toHaveFocus());
});

// The link opened 30 minutes or more before the page takes its token: the token kept for the tab has lapsed.
test('a token kept more than 30 minutes before the page opens is not posted', async () => {
  vi.useFakeTimers({ toFake: ['Date'] });
  const opened = new Date('2026-11-22T10:00:00Z');
  vi.setSystemTime(opened);
  await createAppRouter(createMemoryHistory()).push(link);
  vi.setSystemTime(opened.getTime() + 31 * 60_000);

  await openPage('/invitations/accept?code=abc&state=def');

  expect(
    await screen.findByRole('heading', { level: 1, name: t('organisation.accept.missing.title') }),
  ).toBeInTheDocument();
  expect(acceptances()).toEqual([]);
  // Our two keys are gone; vue-router keeps its own scroll positions in sessionStorage beside them.
  expect(sessionStorage.getItem('invitation-token')).toBeNull();
  expect(sessionStorage.getItem('invitation-token-kept-at')).toBeNull();
});
