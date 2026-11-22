import { render, screen, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import { setLanguage, t } from '@/i18n';
import ThemeToggle from './ThemeToggle.vue';

// jsdom has no matchMedia: the system's preference is this stand-in, which a test changes.
const system = { dark: false, listeners: new Set<() => void>() };

function systemPrefers(theme: 'light' | 'dark') {
  system.dark = theme === 'dark';
  system.listeners.forEach((listener) => {
    listener();
  });
}

beforeEach(() => {
  vi.stubGlobal('matchMedia', (query: string) => ({
    get matches() {
      return system.dark && query === '(prefers-color-scheme: dark)';
    },
    addEventListener: (_: string, listener: () => void) => system.listeners.add(listener),
    removeEventListener: (_: string, listener: () => void) => system.listeners.delete(listener),
  }));
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
  localStorage.clear();
  document.documentElement.removeAttribute('class');
  system.dark = false;
  system.listeners.clear();
});

const dark = () => document.documentElement.classList.contains('dark');
type ThemeKey = 'theme.light' | 'theme.dark' | 'theme.system';
const option = (key: ThemeKey) =>
  within(screen.getByRole('group', { name: t('theme.label') })).getByRole('button', { name: t(key) });

test("with no choice stored the app follows the system's theme, also as it changes", () => {
  systemPrefers('dark');

  render(ThemeToggle);

  expect(option('theme.system')).toHaveAttribute('aria-pressed', 'true');
  expect(dark()).toBe(true);
  systemPrefers('light');
  expect(dark()).toBe(false);
});

test('a choice is shown at once and remembered for the next visit', async () => {
  systemPrefers('dark');
  const { unmount } = render(ThemeToggle);

  await userEvent.click(option('theme.light'));

  expect(dark()).toBe(false);
  expect(option('theme.light')).toHaveAttribute('aria-pressed', 'true');
  unmount();
  render(ThemeToggle);
  expect(option('theme.light')).toHaveAttribute('aria-pressed', 'true');
  expect(dark()).toBe(false);
});

test('choosing system again forgets the choice', async () => {
  render(ThemeToggle);
  await userEvent.click(option('theme.dark'));
  expect(dark()).toBe(true);
  expect(localStorage.getItem('theme')).toBe('dark');

  await userEvent.click(option('theme.system'));

  expect(dark()).toBe(false);
  expect(localStorage.getItem('theme')).toBeNull();
});

test('with storage blocked the toggle still renders and switches the theme', async () => {
  const blocked = () => {
    throw new DOMException('Storage is blocked.', 'SecurityError');
  };
  vi.spyOn(Storage.prototype, 'getItem').mockImplementation(blocked);
  vi.spyOn(Storage.prototype, 'setItem').mockImplementation(blocked);
  render(ThemeToggle);
  expect(option('theme.system')).toHaveAttribute('aria-pressed', 'true');

  await userEvent.click(option('theme.dark'));

  expect(option('theme.dark')).toHaveAttribute('aria-pressed', 'true');
  expect(dark()).toBe(true);
});

test('the toggle speaks German by default', () => {
  render(ThemeToggle);

  const group = within(screen.getByRole('group', { name: 'Farbschema' }));
  expect(group.getAllByRole('button').map((button) => button.textContent.trim())).toEqual(['Hell', 'Dunkel', 'System']);
});

test('the toggle follows a change of language while it is shown', async () => {
  render(ThemeToggle);
  expect(screen.getByRole('group', { name: t('theme.label') })).toBeInTheDocument();

  setLanguage('en-GB', { remember: false });

  const group = within(await screen.findByRole('group', { name: 'Theme' }));
  expect(group.getAllByRole('button').map((button) => button.textContent.trim())).toEqual(['Light', 'Dark', 'System']);
});
