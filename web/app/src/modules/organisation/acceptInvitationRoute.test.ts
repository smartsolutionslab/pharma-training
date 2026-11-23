import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import { createMemoryHistory, createWebHistory } from 'vue-router';
import { createAppRouter } from '@/router';

// The invitation mail links to /invitations/accept#token=<token> (ADR 0004). The organisation module's entry page at
// that address takes the token out of the address before anything else loads (its route's beforeEnter): the browser's
// history, synced tabs and every script reading window.location never see it after the first navigation, and it is kept
// for this tab alone, in sessionStorage under invitation-token.
const token = 'k7Yq2_Lm-9XwPz4RtV8sNc1HjD6bFgUe0OiA5yQrTmW';
const key = 'invitation-token';
const link = `/invitations/accept#token=${token}`;

beforeEach(() => {
  sessionStorage.clear();
  localStorage.clear();
});
afterEach(() => {
  vi.restoreAllMocks();
  sessionStorage.clear();
  localStorage.clear();
  window.history.replaceState({}, '', '/');
});

/** Every value kept in localStorage, which outlives the tab and every other tab shares. */
const localValues = () =>
  Array.from({ length: localStorage.length }, (_, index) => localStorage.key(index)).map((name) =>
    name === null ? '' : `${name}=${localStorage.getItem(name) ?? ''}`,
  );

test("the link's address is the organisation module's page, its token kept for the tab and its fragment gone", async () => {
  const router = createAppRouter(createMemoryHistory());

  await router.push(link);

  expect(router.currentRoute.value.matched).not.toHaveLength(0);
  expect(router.currentRoute.value.path).toBe('/invitations/accept');
  expect(router.currentRoute.value.hash).toBe('');
  expect(router.currentRoute.value.fullPath).not.toContain(token);
  expect(sessionStorage.getItem(key)).toBe(token);
  expect(localValues().join('\n')).not.toContain(token);
});

// The browser opens the link: the address holds the fragment until the router's first navigation, which replaces the
// current entry (history.replaceState) with the address without it. No entry with the token is pushed.
test("with the browser's history the address and the current entry lose the fragment on the first navigation", async () => {
  window.history.replaceState({}, '', link);
  const entries = window.history.length;
  const history = createWebHistory();
  const pushState = vi.spyOn(window.history, 'pushState');
  const replaceState = vi.spyOn(window.history, 'replaceState');
  const router = createAppRouter(history);

  // As app.use(router) starts it: the first navigation, to the address the browser opened.
  await router.push(history.location);
  await router.isReady();

  expect(window.location.pathname).toBe('/invitations/accept');
  expect(window.location.hash).toBe('');
  expect(window.location.href).not.toContain(token);
  expect(window.history.length).toBe(entries);
  expect(pushState).not.toHaveBeenCalled();
  expect(replaceState).toHaveBeenCalled();
  expect(String(replaceState.mock.lastCall?.[2])).not.toContain(token);
  expect(JSON.stringify(window.history.state)).not.toContain(token);
  expect(sessionStorage.getItem(key)).toBe(token);
  expect(localValues().join('\n')).not.toContain(token);
});

test('a malformed token is not kept, and leaves the address as well', async () => {
  sessionStorage.setItem(key, token);
  const router = createAppRouter(createMemoryHistory());

  await router.push(`/invitations/accept#token=${token.slice(3)}`);

  expect(router.currentRoute.value.path).toBe('/invitations/accept');
  expect(router.currentRoute.value.hash).toBe('');
  expect(sessionStorage.getItem(key)).toBeNull();
});

// Back from the identity provider: /invitations/accept?code=…&state=…, no fragment; the token kept before the sign-in
// is still there for the page.
test('the address the sign-in returns to keeps the token kept before it', async () => {
  sessionStorage.setItem(key, token);
  const router = createAppRouter(createMemoryHistory());

  await router.push('/invitations/accept?code=abc&state=def');

  expect(router.currentRoute.value.matched).not.toHaveLength(0);
  expect(router.currentRoute.value.path).toBe('/invitations/accept');
  expect(sessionStorage.getItem(key)).toBe(token);
});
