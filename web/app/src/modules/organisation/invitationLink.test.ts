import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import { createMemoryHistory, createRouter, type RouteLocationNormalized, START_LOCATION } from 'vue-router';
import { captureInvitationLink, invitationTokenSchema, takeInvitationToken } from './invitationLink';

// The invitation mail links to /invitations/accept#token=<token> (ADR 0004): 32 random bytes in base64url, 43
// characters of [A-Za-z0-9_-]. The route's guard keeps the token for this tab (sessionStorage, key invitation-token)
// and sends the browser on to the same address without the fragment; the page takes it out of storage in one read.
const token = 'k7Yq2_Lm-9XwPz4RtV8sNc1HjD6bFgUe0OiA5yQrTmW';
const earlier = 'Ea1y-Er_TokenKeptBeforeThisLinkWasOpened012';
const key = 'invitation-token';

// A router of its own with the address the guard is given: the app's router is the page's tests' (acceptInvitationRoute).
const resolver = createRouter({
  history: createMemoryHistory(),
  routes: [{ path: '/invitations/accept', component: { render: () => null } }],
});
const at = (address: string): RouteLocationNormalized => {
  const resolved = resolver.resolve(address);
  return { ...resolved, name: resolved.name ?? undefined };
};

/** What the guard answers for an address, called as vue-router calls a route's beforeEnter on entering it. */
const capture = (address: string) => captureInvitationLink.call(undefined, at(address), START_LOCATION, vi.fn());

beforeEach(() => {
  sessionStorage.clear();
  localStorage.clear();
});
afterEach(() => {
  vi.restoreAllMocks();
  sessionStorage.clear();
  localStorage.clear();
});

test('the schema accepts a token as the mail carries it: 43 URL-safe characters', () => {
  expect(invitationTokenSchema.safeParse(token).success).toBe(true);
  expect(invitationTokenSchema.safeParse(earlier).success).toBe(true);
});

test.each([
  ['one character short', token.slice(1)],
  ['one character long', `${token}A`],
  ['a character outside base64url', `${token.slice(1)}+`],
  ['padding', `${token.slice(1)}=`],
  ['empty', ''],
])('the schema refuses a token %s', (_case, value) => {
  expect(invitationTokenSchema.safeParse(value).success).toBe(false);
});

test("the guard keeps a link's token for this tab and sends the browser on to the address without its fragment", () => {
  const answer = capture(`/invitations/accept#token=${token}`);

  expect(sessionStorage.getItem(key)).toBe(token);
  expect(answer).toMatchObject({ path: '/invitations/accept', replace: true });
  expect(answer).not.toMatchObject({ hash: expect.stringContaining('token') });
  expect(JSON.stringify(answer)).not.toContain(token);
});

// sessionStorage is this tab's alone; localStorage outlives the tab and is shared with every other.
test('the guard never keeps the token in localStorage', () => {
  capture(`/invitations/accept#token=${token}`);

  expect(localStorage.length).toBe(0);
});

test('a malformed token is not kept, a token kept before is dropped, and the fragment still leaves the address', () => {
  sessionStorage.setItem(key, earlier);

  const answer = capture(`/invitations/accept#token=${token.slice(1)}`);

  expect(sessionStorage.getItem(key)).toBeNull();
  expect(answer).toMatchObject({ path: '/invitations/accept', replace: true });
  expect(JSON.stringify(answer)).not.toContain(token.slice(1));
});

test('a fragment without a token drops a token kept before and leaves the address', () => {
  sessionStorage.setItem(key, earlier);

  const answer = capture('/invitations/accept#something=else');

  expect(sessionStorage.getItem(key)).toBeNull();
  expect(answer).toMatchObject({ path: '/invitations/accept', replace: true });
});

// On the way back from the identity provider the address is /invitations/accept?code=…&state=…: no fragment, and the
// token kept before the sign-in must survive it.
test('an address without a fragment lets the navigation go on and leaves the kept token alone', () => {
  sessionStorage.setItem(key, token);

  const answer = capture('/invitations/accept?code=abc&state=def');

  expect([undefined, true]).toContain(answer);
  expect(sessionStorage.getItem(key)).toBe(token);
});

test('with storage blocked the token is lost, and the fragment still leaves the address', () => {
  vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
    throw new DOMException('The storage is blocked.', 'SecurityError');
  });
  vi.spyOn(Storage.prototype, 'removeItem').mockImplementation(() => {
    throw new DOMException('The storage is blocked.', 'SecurityError');
  });

  const answer = capture(`/invitations/accept#token=${token}`);

  expect(answer).toMatchObject({ path: '/invitations/accept', replace: true });
  expect(JSON.stringify(answer)).not.toContain(token);
});

test('taking the token answers it and removes it: a second take finds none', () => {
  sessionStorage.setItem(key, token);

  expect(takeInvitationToken()).toBe(token);
  expect(sessionStorage.getItem(key)).toBeNull();
  expect(takeInvitationToken()).toBeUndefined();
});

// Storage is untrusted input: what is there is checked again as the guard checked it.
test('a stored value that is not a token is not taken, and is removed', () => {
  sessionStorage.setItem(key, 'not-a-token');

  expect(takeInvitationToken()).toBeUndefined();
  expect(sessionStorage.getItem(key)).toBeNull();
});

test('a token kept in localStorage is no token for the page', () => {
  localStorage.setItem(key, token);

  expect(takeInvitationToken()).toBeUndefined();
});

test('with storage blocked there is no token to take', () => {
  vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
    throw new DOMException('The storage is blocked.', 'SecurityError');
  });

  expect(takeInvitationToken()).toBeUndefined();
});
