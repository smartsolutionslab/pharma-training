import type { NavigationGuardWithThis } from 'vue-router';
import { z } from 'zod';

/**
 * An invitation's token as the mail's link carries it (ADR 0004): 32 random bytes in base64url, 43 characters of
 * [A-Za-z0-9_-].
 */
export const invitationTokenSchema = z.string().regex(/^[A-Za-z0-9_-]{43}$/);

/** Where the token waits for the page across the sign-in: this tab's sessionStorage, never localStorage or the URL. */
const key = 'invitation-token';

function forget() {
  try {
    sessionStorage.removeItem(key);
  } catch {
    // Storage is blocked: nothing can have been kept.
  }
}

function keep(token: string) {
  try {
    sessionStorage.setItem(key, token);
  } catch {
    // Storage is blocked: the token is lost, and the page says to open the link again.
  }
}

/**
 * The invitation page's beforeEnter: an address with a fragment (`#token=…`) has its token kept for this tab, any token
 * kept before dropped, and the navigation sent on to the same address without the fragment, replacing the current
 * entry, so the token leaves the address and the browser's history before anything else loads. An address without a
 * fragment (the way back from the sign-in, `?code&state`) goes on and leaves a kept token alone.
 */
export const captureInvitationLink: NavigationGuardWithThis<undefined> = (to) => {
  if (to.hash === '') {
    return true;
  }
  const token = invitationTokenSchema.safeParse(new URLSearchParams(to.hash.slice(1)).get('token'));
  forget();
  if (token.success) {
    keep(token.data);
  }
  return { path: to.path, query: to.query, replace: true };
};

/** The kept token, taken out of storage in the same call; none when there is none, or what is there is no token. */
export function takeInvitationToken(): string | undefined {
  let stored: string | null;
  try {
    stored = sessionStorage.getItem(key);
  } catch {
    return undefined;
  }
  forget();
  const token = invitationTokenSchema.safeParse(stored);
  return token.success ? token.data : undefined;
}
