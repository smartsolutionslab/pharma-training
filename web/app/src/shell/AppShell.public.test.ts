import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { cleanup, render, screen, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { User } from 'oidc-client-ts';
import { createPinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest';
import { computed, defineComponent, h, type Plugin } from 'vue';
import { createMemoryHistory } from 'vue-router';
import App from '@/App.vue';
import { signedInAsDeveloper } from '@/auth/signedInAsDeveloper';
import SignedIn from '@/auth/SignedIn.vue';
import { type Auth, type AuthClient, authKey, createAuth } from '@/auth/useAuth';
import { t } from '@/i18n';
import { createAppRouter } from '@/router';

// A public page (#54), /verify/:code, which a certificate's QR code opens: App.vue renders it in the public layout, a
// header with the app's name and the language group above the page, and none of the shell. Nothing of the shell asks
// the host anything: GET /me and GET /organisation/pharmacies/mine would answer 401 to a visitor who is signed out.
// The page renders as it is, without a stand-in, and the user menu and the memberships are not stood in for either:
// the host below records every request, so a request the shell would send shows.

const code = 'AbCdEfGhIjKlMnOpQrStUvWxYz012-_9';
const verifyAddress = `/verify/${code}`;

// The host as the API client reaches it: /me and the memberships answer as for the developer, so that a shell, were
// it rendered, would render whole; the verification waits for an answer that never comes, since this file checks the
// layout, not the page. Every request is recorded.
const requests: string[] = [];
const host = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>();
vi.stubGlobal('fetch', host);

beforeEach(() => {
  requests.length = 0;
  localStorage.clear();
  host.mockImplementation(async (input) => {
    const url = String(input);
    requests.push(new URL(url, 'http://localhost:3000').pathname);
    if (url.includes('/api/me')) {
      return Response.json({ name: 'Local Developer' });
    }
    if (url.includes('/api/organisation/pharmacies/mine')) {
      return Response.json({ items: [], totalCount: 0, page: 1, pageSize: 100 });
    }
    return new Promise<Response>(() => undefined);
  });
});
afterEach(cleanup);
afterEach(() => {
  host.mockReset();
  localStorage.clear();
});

// A visitor who is signed out: no user, and nothing under way.
const signedOut: Plugin = {
  install(app) {
    const auth: Auth = {
      user: computed(() => null),
      isAuthenticated: computed(() => false),
      isLoading: computed(() => false),
      error: computed(() => null),
      activeNavigator: computed(() => null),
      signinRedirect: () => Promise.resolve(),
      signoutRedirect: () => Promise.resolve(),
    };
    app.provide(authKey, auth);
  },
};

async function renderAt(path: string, auth: Plugin) {
  const router = createAppRouter(createMemoryHistory());
  await router.push(path);
  render(App, {
    global: { plugins: [router, createPinia(), [VueQueryPlugin, { queryClient: new QueryClient() }], auth] },
  });
  return { router };
}

const visitors = [
  { visitor: 'signed out', auth: signedOut },
  { visitor: 'signed in', auth: signedInAsDeveloper },
];

describe.each(visitors)('a visitor $visitor at a verification link', ({ auth }) => {
  test('sees the page in the public layout: a header with the app and the languages, no shell', async () => {
    await renderAt(verifyAddress, auth);

    expect(await screen.findByRole('heading', { level: 1, name: 'Bestätigung prüfen' })).toBeInTheDocument();
    const header = screen.getByRole('banner');
    expect(within(header).getByText('pharma-training')).toBeInTheDocument();
    expect(within(header).getByRole('group', { name: t('language.label') })).toBeInTheDocument();
    expect(within(header).getByRole('button', { name: 'Deutsch' })).toHaveAttribute('aria-pressed', 'true');
    expect(within(header).getByRole('button', { name: 'English' })).toBeInTheDocument();

    // No sidebar, no navigation of the app, no breadcrumbs, no palette, no pharmacy switcher.
    expect(screen.queryAllByRole('navigation')).toEqual([]);
    expect(screen.queryByRole('button', { name: t('ui.sidebar.toggle') })).not.toBeInTheDocument();
    expect(screen.queryByText(t('shell.palette.open'))).not.toBeInTheDocument();
    expect(screen.queryByText(t('shell.gate.notAvailable'))).not.toBeInTheDocument();
  });

  test('asks the host neither who the user is nor their pharmacies', async () => {
    await renderAt(verifyAddress, auth);
    // Once the page is there, its layout is too: a shell mounted with it would have asked the host by now.
    await screen.findByRole('heading', { level: 1, name: 'Bestätigung prüfen' });

    expect(requests.filter((path) => path === '/api/me' || path === '/api/organisation/pharmacies/mine')).toEqual([]);
  });

  test("switches the page's language from the header", async () => {
    const user = userEvent.setup();
    await renderAt(verifyAddress, auth);
    await screen.findByRole('heading', { level: 1, name: 'Bestätigung prüfen' });

    await user.click(within(screen.getByRole('banner')).getByRole('button', { name: 'English' }));

    expect(await screen.findByRole('heading', { level: 1, name: 'Verify a certificate' })).toBeInTheDocument();
  });
});

// Leaving the public page for a page of the app shows the shell again.
test('a signed-in user who leaves the verification page for a page of the app sees the shell', async () => {
  const { router } = await renderAt(verifyAddress, signedInAsDeveloper);
  await screen.findByRole('heading', { level: 1, name: 'Bestätigung prüfen' });

  await router.push('/');

  expect(await screen.findByRole('navigation', { name: t('shell.navigation.label') })).toBeInTheDocument();
});

// Review of #54. As main.ts mounts it: SignedIn around App, both waiting for the router's first navigation. When that
// navigation fails (a guard throws), SignedIn goes ahead once the user is loaded, and App, which mounts only then, asks
// the router whether it is ready: a router whose first navigation failed is never ready, and App would stay blank.
test('a signed-in user whose first navigation fails sees the shell', async () => {
  const developer = new User({
    access_token: 'access-token',
    token_type: 'Bearer',
    profile: {
      sub: 'developer-id',
      iss: 'http://localhost:8080/realms/pharma-training',
      aud: 'pharma-training-web',
      exp: 0,
      iat: 0,
      preferred_username: 'developer',
      realm_access: { roles: ['manager'] },
    },
    expires_at: Math.floor(Date.now() / 1000) + 300,
  });
  const fake: AuthClient = {
    getUser: vi.fn<AuthClient['getUser']>(() => Promise.resolve(developer)),
    signinCallback: vi.fn<AuthClient['signinCallback']>(() => Promise.resolve(developer)),
    signinRedirect: vi.fn<AuthClient['signinRedirect']>(() => Promise.resolve()),
    signoutRedirect: vi.fn<AuthClient['signoutRedirect']>(() => Promise.resolve()),
    events: {
      addUserLoaded: vi.fn(),
      addUserUnloaded: vi.fn(),
      addAccessTokenExpired: vi.fn(),
      addSilentRenewError: vi.fn(),
    },
  };
  // The history starts at the address: installing the router starts its first navigation from there.
  const history = createMemoryHistory();
  history.replace('/certification');
  const router = createAppRouter(history);
  const guard = vi.fn();
  guard.mockImplementationOnce(() => {
    throw new Error('A guard failed.');
  });
  router.beforeEach(guard);
  router.onError(() => undefined);
  const root = defineComponent({ render: () => h(SignedIn, null, { default: () => h(App) }) });

  render(root, {
    global: {
      plugins: [router, createPinia(), [VueQueryPlugin, { queryClient: new QueryClient() }]],
      provide: { [authKey]: createAuth(fake) },
    },
  });
  await vi.waitFor(() => expect(guard).toHaveBeenCalledOnce());

  expect(await screen.findByRole('navigation', { name: t('shell.navigation.label') })).toBeInTheDocument();
  expect(fake.signinRedirect).not.toHaveBeenCalled();
});
