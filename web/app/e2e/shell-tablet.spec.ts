import { expect, type Page, test } from '@playwright/test';

// A tablet held upright (docs/domain/README.md, "Geräte"): the desktop sidebar's breakpoint, 768 px.
test.use({ viewport: { width: 768, height: 1024 } });

// The developer's pharmacies, as GET /organisation/pharmacies/mine answers them: two, whatever the database holds.
const pharmacies = [
  {
    id: '0198c1a0-0000-7000-8000-0000000000a1',
    name: 'Apotheke am Markt',
    membershipId: '0198c1a0-0000-7000-8000-0000000000b1',
    role: 'Manager',
    jobFunction: 'Pharmacist',
    canManageMembers: true,
    canAssignCourses: true,
  },
  {
    id: '0198c1a0-0000-7000-8000-0000000000a2',
    name: 'Apotheke am Dom',
    membershipId: '0198c1a0-0000-7000-8000-0000000000b2',
    role: 'Employee',
    jobFunction: 'Pta',
    canManageMembers: false,
    canAssignCourses: false,
  },
];

async function signIn(page: Page) {
  await page.route(/\/api\/organisation\/pharmacies\/mine(\?|$)/, (route) =>
    route.fulfill({ json: { items: pharmacies, totalCount: pharmacies.length, page: 1, pageSize: 100 } }),
  );
  await page.goto('/');
  await page.getByLabel('Username or email').fill('developer');
  await page.getByLabel('Password', { exact: true }).fill('developer');
  await page.getByRole('button', { name: 'Sign In' }).click();
  await expect(page.getByText('Signed in as developer')).toBeVisible();
}

/** Whether the page is wider than the viewport, so the user would scroll sideways. */
const scrollsSideways = (page: Page) =>
  page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth);

test('on a tablet the user chooses a pharmacy, switches to another, and it stays chosen after a reload', async ({
  page,
}) => {
  await signIn(page);

  await page
    .getByRole('region', { name: 'Choose your pharmacy' })
    .getByRole('button', { name: /Apotheke am Markt/ })
    .click();
  const navigation = page.getByRole('navigation', { name: 'Modules' });
  await expect(navigation.getByRole('link', { name: 'Organisation' })).toBeVisible();
  expect(await scrollsSideways(page)).toBe(false);

  await page.getByRole('button', { name: 'Pharmacy: Apotheke am Markt' }).click();
  await page
    .getByRole('menu', { name: 'Pharmacies' })
    .getByRole('menuitemradio', { name: /Apotheke am Dom/ })
    .click();
  await expect(page.getByRole('button', { name: 'Pharmacy: Apotheke am Dom' })).toBeVisible();
  await expect(navigation.getByRole('link', { name: 'Organisation' })).toBeHidden();
  await expect(navigation.getByRole('link', { name: 'Learning' })).toBeVisible();

  await page.reload();
  await expect(page.getByRole('button', { name: 'Pharmacy: Apotheke am Dom' })).toBeVisible();
  await expect(navigation.getByRole('link', { name: 'Learning' })).toBeVisible();
  expect(await scrollsSideways(page)).toBe(false);

  await navigation.getByRole('link', { name: 'Learning' }).click();
  await expect(page).toHaveURL(/\/learning$/);
  // The page itself, not the gate: an employee of Apotheke am Dom may learn.
  await expect(page.getByRole('heading', { name: 'Samples' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Not available' })).toBeHidden();
  expect(await scrollsSideways(page)).toBe(false);
});
