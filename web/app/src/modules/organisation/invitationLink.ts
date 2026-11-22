import type { NavigationGuardWithThis, RouteLocationNormalized, RouteLocationRaw } from 'vue-router';
import { z } from 'zod';

/**
 * An invitation's token as the mail's link carries it (ADR 0004): 32 random bytes in base64url, 43 characters of
 * [A-Za-z0-9_-].
 */
export const invitationTokenSchema = z.string().regex(/^[A-Za-z0-9_-]{43}$/);

/** Where the token waits for the page across the sign-in: this tab's sessionStorage, never localStorage or the URL. */
const key = 'invitation-token';

/** When the link whose token is kept was opened, in milliseconds since the epoch, beside the token. */
const keptAtKey = 'invitation-token-kept-at';

/**
 * How long a kept token waits for the page: 30 minutes from the moment its link was opened, long enough to sign in or
 * to register and verify the address, not long enough for a tab left open to accept it hours later.
 */
const lifetime = 30 * 60_000;

function forget() {
  try {
    sessionStorage.removeItem(key);
    sessionStorage.removeItem(keptAtKey);
  } catch {
    // Storage is blocked: nothing can have been kept.
  }
}

function keep(token: string) {
  try {
    sessionStorage.setItem(key, token);
    sessionStorage.setItem(keptAtKey, String(Date.now()));
  } catch {
    // Storage is blocked: the token is lost, and the page says to open the link again.
    forget();
  }
}

/**
 * Keeps the token an address's fragment (`#token=…`) carries for this tab, with the time it was kept, and drops any
 * kept before; answers the same address without the fragment, replacing the current entry, or true for an address
 * without a fragment (the way back from the sign-in, `?code&state`), which leaves a kept token alone.
 */
export function captureFrom(to: RouteLocationNormalized): true | RouteLocationRaw {
  if (to.hash === '') {
    return true;
  }
  const token = invitationTokenSchema.safeParse(new URLSearchParams(to.hash.slice(1)).get('token'));
  forget();
  if (token.success) {
    keep(token.data);
  }
  return { path: to.path, query: to.query, replace: true };
}

/**
 * The invitation page's beforeEnter: an address with a fragment has its token kept and the navigation sent on to the
 * same address without it (captureFrom), so the token leaves the address and the browser's history before anything
 * else loads. A second link opened while the page is shown changes only the fragment, which runs no beforeEnter: the
 * page captures it in its own beforeRouteUpdate.
 */
export const captureInvitationLink: NavigationGuardWithThis<undefined> = (to) => captureFrom(to);

/**
 * The kept token, taken out of storage in the same call; none when there is none, what is there is no token, or it
 * was kept without its time or 30 minutes or more ago. Storage is left without a token either way.
 */
export function takeInvitationToken(): string | undefined {
  let stored: string | null;
  let keptAt: string | null;
  try {
    stored = sessionStorage.getItem(key);
    keptAt = sessionStorage.getItem(keptAtKey);
  } catch {
    return undefined;
  }
  forget();
  const token = invitationTokenSchema.safeParse(stored);
  const age = Date.now() - Number(keptAt);
  if (!token.success || keptAt === null || !Number.isFinite(age) || age < 0 || age >= lifetime) {
    return undefined;
  }
  return token.data;
}
