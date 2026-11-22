import { render, screen } from '@testing-library/vue';
import { User } from 'oidc-client-ts';
import { afterEach, expect, test, vi } from 'vitest';
import { setLanguage, t } from '@/i18n';
import SignedIn from './SignedIn.vue';
import { type AuthClient, authKey, createAuth } from './useAuth';

afterEach(() => {
  window.history.replaceState({}, '', '/');
});

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
  },
  expires_at: Math.floor(Date.now() / 1000) + 300,
});

/** oidc-client-ts's UserManager as SignedIn sees it: a stored user (or none), a sign-in callback and the redirects. */
function client(overrides: Partial<AuthClient> = {}): AuthClient {
  return {
    getUser: vi.fn<AuthClient['getUser']>(() => Promise.resolve(null)),
    signinCallback: vi.fn<AuthClient['signinCallback']>(() => Promise.resolve(developer)),
    signinRedirect: vi.fn<AuthClient['signinRedirect']>(() => Promise.resolve()),
    signoutRedirect: vi.fn<AuthClient['signoutRedirect']>(() => Promise.resolve()),
    events: {
      addUserLoaded: vi.fn(),
      addUserUnloaded: vi.fn(),
      addAccessTokenExpired: vi.fn(),
      addSilentRenewError: vi.fn(),
    },
    ...overrides,
  };
}

function renderSignedIn(fake: AuthClient) {
  return render(SignedIn, {
    global: { provide: { [authKey]: createAuth(fake) } },
    slots: { default: '<h1>pharma-training</h1>' },
  });
}

test('an anonymous visitor is sent to the identity provider', async () => {
  const fake = client();

  renderSignedIn(fake);

  await vi.waitFor(() => expect(fake.signinRedirect).toHaveBeenCalledOnce());
  expect(screen.getByText(t('auth.signingIn'))).toBeInTheDocument();
  expect(screen.queryByRole('heading', { name: 'pharma-training' })).not.toBeInTheDocument();
});

// Who is signed in, and Sign out, are in the app shell's user menu (UserMenu.vue), not above the application.
test('a signed-in user sees the application', async () => {
  const fake = client({ getUser: vi.fn<AuthClient['getUser']>(() => Promise.resolve(developer)) });

  renderSignedIn(fake);

  expect(await screen.findByRole('heading', { name: 'pharma-training' })).toBeInTheDocument();
  expect(screen.queryByText(t('auth.signedInAs', { name: 'developer' }))).not.toBeInTheDocument();
  expect(fake.signinRedirect).not.toHaveBeenCalled();
});

test('coming back from the identity provider completes the sign-in and drops its parameters from the address', async () => {
  window.history.replaceState({}, '', '/?code=the-code&state=the-state');
  const fake = client();

  renderSignedIn(fake);

  expect(await screen.findByRole('heading', { name: 'pharma-training' })).toBeInTheDocument();
  expect(fake.signinCallback).toHaveBeenCalledOnce();
  expect(window.location.search).toBe('');
  expect(fake.signinRedirect).not.toHaveBeenCalled();
});

test('a failed sign-in is shown, not retried', async () => {
  window.history.replaceState({}, '', '/?error=access_denied&state=the-state');
  const fake = client({
    signinCallback: vi.fn<AuthClient['signinCallback']>(() => Promise.reject(new Error('access_denied'))),
  });

  renderSignedIn(fake);

  expect(await screen.findByRole('alert')).toHaveTextContent(t('auth.signInFailed', { message: 'access_denied' }));
  expect(fake.signinRedirect).not.toHaveBeenCalled();
});

test('a session whose access token expires sends the user back to the identity provider', async () => {
  const fake = client({ getUser: vi.fn<AuthClient['getUser']>(() => Promise.resolve(developer)) });
  renderSignedIn(fake);
  expect(await screen.findByRole('heading', { name: 'pharma-training' })).toBeInTheDocument();

  const [expired] = vi.mocked(fake.events.addAccessTokenExpired).mock.calls[0] ?? [];
  expired?.();

  await vi.waitFor(() => expect(fake.signinRedirect).toHaveBeenCalledOnce());
  expect(screen.queryByRole('heading', { name: 'pharma-training' })).not.toBeInTheDocument();
});

test('a failed silent renewal is shown', async () => {
  const fake = client({ getUser: vi.fn<AuthClient['getUser']>(() => Promise.resolve(developer)) });
  renderSignedIn(fake);
  expect(await screen.findByRole('heading', { name: 'pharma-training' })).toBeInTheDocument();

  const [renewalFailed] = vi.mocked(fake.events.addSilentRenewError).mock.calls[0] ?? [];
  renewalFailed?.(new Error('the identity provider is unreachable'));

  expect(await screen.findByRole('alert')).toHaveTextContent(
    t('auth.signInFailed', { message: 'the identity provider is unreachable' }),
  );
});

test('a redirect to the identity provider that fails is shown', async () => {
  const fake = client({
    signinRedirect: vi.fn<AuthClient['signinRedirect']>(() => Promise.reject(new Error('discovery failed'))),
  });

  renderSignedIn(fake);

  expect(await screen.findByRole('alert')).toHaveTextContent(t('auth.signInFailed', { message: 'discovery failed' }));
});

test('signing out does not start a sign-in while the identity provider signs the user out', async () => {
  const fake = client({ getUser: vi.fn<AuthClient['getUser']>(() => Promise.resolve(developer)) });
  // oidc-client-ts unloads the user before it navigates to the identity provider's logout page.
  fake.signoutRedirect = vi.fn<AuthClient['signoutRedirect']>(() => {
    const [unloaded] = vi.mocked(fake.events.addUserUnloaded).mock.calls[0] ?? [];
    unloaded?.();
    return Promise.resolve();
  });
  const auth = createAuth(fake);
  render(SignedIn, { global: { provide: { [authKey]: auth } }, slots: { default: '<h1>pharma-training</h1>' } });
  await screen.findByRole('heading', { name: 'pharma-training' });

  // As the user menu's Sign out does.
  await auth.signoutRedirect();

  await vi.waitFor(() => expect(fake.signoutRedirect).toHaveBeenCalledOnce());
  expect(fake.signinRedirect).not.toHaveBeenCalled();
  expect(await screen.findByText(t('auth.signingOut'))).toBeInTheDocument();
  expect(fake.signinRedirect).not.toHaveBeenCalled();
});

test('the sign-in texts are German by default and English once chosen', async () => {
  renderSignedIn(client());
  expect(await screen.findByText('Anmeldung läuft…')).toBeInTheDocument();

  setLanguage('en-GB', { remember: false });

  expect(await screen.findByText('Signing in…')).toBeInTheDocument();
});
