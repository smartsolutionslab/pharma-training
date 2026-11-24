import { afterEach, beforeEach, expect, test, vi } from 'vitest';

// The edge's GET /config.json, as the generated client asks it (src/api/generated.ts, from openapi.json): the test says
// what it answers. Nothing else may reach the host: fetch fails the test.
const { getConfigJson } = vi.hoisted(() => ({ getConfigJson: vi.fn<() => Promise<unknown>>() }));
vi.mock('@/api/generated', () => ({ getConfigJson }));

const fetchToHost = vi.fn<typeof fetch>(() => Promise.reject(new Error('runtimeConfig must not call fetch itself.')));

beforeEach(() => {
  vi.resetModules();
  vi.stubGlobal('fetch', fetchToHost);
});
afterEach(() => {
  getConfigJson.mockReset();
  fetchToHost.mockClear();
  vi.unstubAllGlobals();
});

const deployed = {
  oidcAuthority: 'https://auth.example.test/realms/pharma-training',
  clientId: 'pharma-training-web',
};

/** src/auth/runtimeConfig.ts, fresh for each test: what it loaded is kept for the page's lifetime. */
const runtimeConfigModule = () => import('./runtimeConfig');

test('it loads the runtime configuration through the generated getConfigJson, not fetch', async () => {
  getConfigJson.mockResolvedValue(deployed);
  const { loadRuntimeConfig, runtimeConfig } = await runtimeConfigModule();

  await expect(loadRuntimeConfig()).resolves.toEqual(deployed);

  expect(getConfigJson).toHaveBeenCalledTimes(1);
  expect(fetchToHost).not.toHaveBeenCalled();
  expect(runtimeConfig()).toEqual(deployed);
});

test('it parses a valid answer', async () => {
  const { runtimeConfigSchema } = await runtimeConfigModule();

  expect(runtimeConfigSchema.safeParse(deployed)).toMatchObject({ success: true, data: deployed });
});

test.each([
  ['no oidcAuthority', { clientId: 'pharma-training-web' }],
  [
    'an oidcAuthority that is no URL',
    { oidcAuthority: 'auth.example.test/realms/pharma-training', clientId: 'pharma-training-web' },
  ],
  ['an empty oidcAuthority', { oidcAuthority: '', clientId: 'pharma-training-web' }],
  ['no clientId', { oidcAuthority: deployed.oidcAuthority }],
  ['an empty clientId', { oidcAuthority: deployed.oidcAuthority, clientId: '' }],
  ['no object at all', 'pharma-training'],
])('it rejects an answer with %s', async (_, answer) => {
  getConfigJson.mockResolvedValue(answer);
  const { loadRuntimeConfig, runtimeConfigSchema } = await runtimeConfigModule();

  expect(runtimeConfigSchema.safeParse(answer).success).toBe(false);
  await expect(loadRuntimeConfig()).rejects.toThrow();
});

test('it rejects when the edge does not answer', async () => {
  getConfigJson.mockRejectedValue(new Error('The API answered 503: '));
  const { loadRuntimeConfig } = await runtimeConfigModule();

  await expect(loadRuntimeConfig()).rejects.toThrow('503');
});

test('reading it before it is loaded fails, naming loadRuntimeConfig', async () => {
  const { runtimeConfig } = await runtimeConfigModule();

  expect(() => runtimeConfig()).toThrow(/loadRuntimeConfig/);
});

test('an answer it rejected is not kept', async () => {
  getConfigJson.mockResolvedValue({ oidcAuthority: deployed.oidcAuthority, clientId: '' });
  const { loadRuntimeConfig, runtimeConfig } = await runtimeConfigModule();

  await expect(loadRuntimeConfig()).rejects.toThrow();

  expect(() => runtimeConfig()).toThrow(/loadRuntimeConfig/);
});
