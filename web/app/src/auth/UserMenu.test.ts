import { render, screen, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { expect, test, vi } from 'vitest';
import { computed } from 'vue';
import { t } from '@/i18n';
import UserMenu from './UserMenu.vue';

// The name is the host's answer to /me; here it is the test's.
const me = vi.hoisted(() => ({ name: undefined as string | undefined }));
vi.mock('@/api/useCurrentUser', () => ({ useCurrentUser: () => computed(() => me.name) }));

const signoutRedirect = vi.hoisted(() => vi.fn(() => Promise.resolve()));
vi.mock('@/auth/useAuth', () => ({ useAuth: () => ({ signoutRedirect }) }));

const languageSwitch = () => screen.getByRole('group', { name: t('language.label') });
const option = (key: 'language.de' | 'language.en') => within(languageSwitch()).getByRole('button', { name: t(key) });

test('the user menu names the signed-in user as /me does', () => {
  me.name = 'developer';
  render(UserMenu);

  expect(screen.getByText(t('auth.signedInAs', { name: 'developer' }))).toBeInTheDocument();
});

test('until /me answers the user menu says only that someone is signed in', () => {
  me.name = undefined;
  render(UserMenu);

  expect(screen.getByText(t('auth.signedIn'))).toBeInTheDocument();
});

test('Sign out signs the user out at the identity provider', async () => {
  const user = userEvent.setup();
  render(UserMenu);

  await user.click(screen.getByRole('button', { name: t('auth.signOut') }));

  expect(signoutRedirect).toHaveBeenCalledOnce();
});

test('the user menu speaks German by default', () => {
  me.name = 'developer';
  render(UserMenu);

  expect(screen.getByText('Angemeldet als developer')).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Abmelden' })).toBeInTheDocument();
});

test('the language switch offers each language in its own words, the current one pressed', () => {
  render(UserMenu);

  expect(within(languageSwitch()).getAllByRole('button')).toHaveLength(2);
  expect(option('language.de')).toHaveTextContent('Deutsch');
  expect(option('language.en')).toHaveTextContent('English');
  expect(option('language.de')).toHaveAttribute('aria-pressed', 'true');
  expect(option('language.en')).toHaveAttribute('aria-pressed', 'false');
});

// Screen readers pronounce each name in its own language.
test('each language on the switch carries its own lang', () => {
  render(UserMenu);

  expect(option('language.de')).toHaveAttribute('lang', expect.stringMatching(/^de(-|$)/));
  expect(option('language.en')).toHaveAttribute('lang', expect.stringMatching(/^en(-|$)/));
});

test('choosing English shows the menu in English at once and remembers the choice', async () => {
  const user = userEvent.setup();
  me.name = 'developer';
  render(UserMenu);
  const german = { signedInAs: t('auth.signedInAs', { name: 'developer' }), signOut: t('auth.signOut') };

  await user.click(option('language.en'));

  expect(await screen.findByText('Signed in as developer')).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Sign out' })).toBeInTheDocument();
  expect(screen.queryByText(german.signedInAs)).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: german.signOut })).not.toBeInTheDocument();
  expect(screen.getByRole('group', { name: 'Language' })).toBeInTheDocument();
  expect(option('language.en')).toHaveAttribute('aria-pressed', 'true');
  expect(option('language.de')).toHaveAttribute('aria-pressed', 'false');
  expect(document.documentElement.lang).toBe('en-GB');
  expect(localStorage.getItem('language')).toBe('en-GB');
});

test('the chosen language holds when the menu is shown again', async () => {
  const user = userEvent.setup();
  me.name = 'developer';
  const { unmount } = render(UserMenu);
  await user.click(option('language.en'));
  await screen.findByText('Signed in as developer');

  unmount();
  render(UserMenu);

  expect(screen.getByText('Signed in as developer')).toBeInTheDocument();
  expect(option('language.en')).toHaveAttribute('aria-pressed', 'true');
});

test('choosing Deutsch again switches back to German', async () => {
  const user = userEvent.setup();
  me.name = 'developer';
  render(UserMenu);
  await user.click(option('language.en'));
  await screen.findByText('Signed in as developer');

  await user.click(option('language.de'));

  expect(await screen.findByText('Angemeldet als developer')).toBeInTheDocument();
  expect(option('language.de')).toHaveAttribute('aria-pressed', 'true');
  expect(document.documentElement.lang).toBe('de-DE');
  expect(localStorage.getItem('language')).toBe('de-DE');
});
