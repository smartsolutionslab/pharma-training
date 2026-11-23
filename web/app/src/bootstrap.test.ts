import { screen } from '@testing-library/vue';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import { de } from '@/i18n/de';
import { en } from '@/i18n/en';

// The edge's GET /config.json through the generated client; the test says what it answers.
const { getConfigJson } = vi.hoisted(() => ({ getConfigJson: vi.fn<() => Promise<unknown>>() }));
vi.mock('@/api/generated', () => ({ getConfigJson }));

let target: HTMLElement;

beforeEach(() => {
  vi.resetModules();
  target = document.createElement('div');
  target.id = 'app';
  document.body.append(target);
});
afterEach(() => {
  getConfigJson.mockReset();
  target.remove();
});

const deployed = {
  oidcAuthority: 'https://auth.example.test/realms/pharma-training',
  clientId: 'pharma-training-web',
};

/** The text a catalog holds under a dotted key, failing the test where it holds none. */
function message(catalog: object, key: string): string {
  const value = key
    .split('.')
    .reduce<unknown>(
      (node, part) => (typeof node === 'object' && node !== null ? (node as Record<string, unknown>)[part] : undefined),
      catalog,
    );
  if (typeof value !== 'string' || value.trim() === '') {
    throw new Error(`The catalog has no text under ${key}.`);
  }
  return value;
}

/** src/bootstrap.ts and the i18n it renders with, fresh for each test: the runtime configuration is loaded once a page. */
async function loadBootstrap(language: 'de-DE' | 'en-GB' = 'de-DE') {
  const { setLanguage } = await import('@/i18n');
  setLanguage(language, { remember: false });
  return import('./bootstrap');
}

test('it mounts the app once the runtime configuration is loaded', async () => {
  getConfigJson.mockResolvedValue(deployed);
  const { bootstrap } = await loadBootstrap();
  const { runtimeConfig } = await import('@/auth/runtimeConfig');
  const configurationAtMount: unknown[] = [];
  const mount = vi.fn((element: HTMLElement) => {
    configurationAtMount.push(runtimeConfig());
    element.textContent = 'the app';
  });

  await bootstrap(target, mount);

  expect(mount).toHaveBeenCalledExactlyOnceWith(target);
  expect(configurationAtMount).toEqual([deployed]);
  expect(screen.queryByRole('alert')).not.toBeInTheDocument();
});

test.each([
  ['the edge does not answer', () => getConfigJson.mockRejectedValue(new Error('The API answered 503: '))],
  ['the answer is invalid', () => getConfigJson.mockResolvedValue({ oidcAuthority: 'not a url', clientId: '' })],
])('when %s, it renders the error alert and no app', async (_, answer) => {
  answer();
  const { bootstrap } = await loadBootstrap();
  const mount = vi.fn();

  await bootstrap(target, mount);

  const alert = await screen.findByRole('alert');
  expect(alert).toHaveTextContent(message(de, 'auth.configurationUnavailable'));
  expect(target).toContainElement(alert);
  expect(mount).not.toHaveBeenCalled();
});

test("the error speaks the user's language", async () => {
  getConfigJson.mockRejectedValue(new Error('The API answered 503: '));
  const { bootstrap } = await loadBootstrap('en-GB');

  await bootstrap(target, vi.fn());

  expect(await screen.findByRole('alert')).toHaveTextContent(message(en, 'auth.configurationUnavailable'));
  expect(message(en, 'auth.configurationUnavailable')).not.toBe(message(de, 'auth.configurationUnavailable'));
});
