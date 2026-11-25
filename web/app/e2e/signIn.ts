import { expect, type Page } from '@playwright/test';

/**
 * Signs in on Keycloak's form, wherever the app sent the browser to it from, as a local user of the realm, whose
 * password is their name; resolves to the bearer token the app then sends (`Authorization`'s value).
 *
 * The wait is the app's own question, `GET /api/me`, answered with success: it leaves once Keycloak has checked the
 * password and the app has its token, which under load takes longer than an assertion's five seconds (#430), and the
 * test's timeout bounds it. An answer that fails first (a token not yet stored) does not end the wait; the user menu
 * then names the user, in German or English, whichever language the app is in.
 */
export async function submitSignIn(page: Page, user: string) {
  await page.getByLabel('Username or email').fill(user);
  await page.getByLabel('Password', { exact: true }).fill(user);
  const me = page.waitForResponse(
    (response) =>
      new URL(response.url()).pathname === '/api/me' && response.request().method() === 'GET' && response.ok(),
  );
  await page.getByRole('button', { name: 'Sign In' }).click();
  const answer = await me;
  const escaped = user.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  await expect(page.getByText(new RegExp(`^(Angemeldet als|Signed in as) ${escaped}$`))).toBeVisible();
  return answer.request().headers()['authorization'] ?? '';
}

/** Opens the app, which sends the browser to Keycloak, and signs in there: see `submitSignIn`. */
export async function signIn(page: Page, user = 'developer') {
  await page.goto('/');
  return submitSignIn(page, user);
}
