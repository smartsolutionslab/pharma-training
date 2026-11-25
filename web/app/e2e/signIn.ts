import { expect, type Page } from '@playwright/test';

/**
 * Signs in on Keycloak's form as a local user of the realm, whose password is their name, and waits until the app knows
 * who signed in. The wait is the app's own question, `GET /api/me`: it leaves once Keycloak has checked the password and
 * the app has its token, which under load takes longer than an assertion's five seconds, and the test's timeout bounds
 * it. A failed answer fails here with its status, not later as text that never shows.
 */
export async function signIn(page: Page, user = 'developer') {
  await page.goto('/');
  await page.getByLabel('Username or email').fill(user);
  await page.getByLabel('Password', { exact: true }).fill(user);
  const me = page.waitForResponse(
    (response) => new URL(response.url()).pathname === '/api/me' && response.request().method() === 'GET',
  );
  await page.getByRole('button', { name: 'Sign In' }).click();
  const answer = await me;
  expect(answer.status(), 'GET /api/me after signing in').toBe(200);
  await expect(page.getByText(`Angemeldet als ${user}`)).toBeVisible();
}
