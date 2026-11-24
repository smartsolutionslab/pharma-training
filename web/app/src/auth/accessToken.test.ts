import { User } from 'oidc-client-ts';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';

const { getConfigJson } = vi.hoisted(() => ({ getConfigJson: vi.fn<() => Promise<unknown>>() }));
vi.mock('@/api/generated', () => ({ getConfigJson }));

beforeEach(() => {
  vi.resetModules();
});
afterEach(() => {
  getConfigJson.mockReset();
  sessionStorage.clear();
});

const deployed = {
  oidcAuthority: 'https://auth.example.test/realms/pharma-training',
  clientId: 'pharma-training-web',
};

/** A signed-in user as oidc-client-ts stores it in sessionStorage, under oidc.user:<authority>:<client id>. */
function storeUser(authority: string, clientId: string, accessToken: string) {
  const user = new User({
    access_token: accessToken,
    token_type: 'Bearer',
    profile: { sub: 'developer-id', iss: authority, aud: clientId, exp: 0, iat: 0 },
    expires_at: Math.floor(Date.now() / 1000) + 300,
  });
  sessionStorage.setItem(`oidc.user:${authority}:${clientId}`, user.toStorageString());
}

test('it reads the user stored for the runtime authority and client', async () => {
  getConfigJson.mockResolvedValue(deployed);
  const { loadRuntimeConfig } = await import('./runtimeConfig');
  const { accessToken } = await import('./accessToken');
  await loadRuntimeConfig();
  storeUser(deployed.oidcAuthority, deployed.clientId, 'runtime-token');

  expect(accessToken()).toBe('runtime-token');
});

test('it answers no token before the runtime configuration is loaded, without falling back to a built-in authority', async () => {
  // The generated getConfigJson goes through apiFetch, which asks for a token before the configuration exists.
  storeUser('http://localhost:8080/realms/pharma-training', 'pharma-training-web', 'built-in-token');
  const { accessToken } = await import('./accessToken');

  expect(accessToken()).toBeUndefined();
});

test("it ignores a user stored for another environment's authority", async () => {
  getConfigJson.mockResolvedValue(deployed);
  const { loadRuntimeConfig } = await import('./runtimeConfig');
  const { accessToken } = await import('./accessToken');
  await loadRuntimeConfig();
  storeUser('http://localhost:8080/realms/pharma-training', deployed.clientId, 'other-token');

  expect(accessToken()).toBeUndefined();
});
