import { expect, type Locator, type Page, test } from '@playwright/test';
import {
  courseTitle,
  daysAhead,
  developer,
  pharmacyName,
  publishCourse,
  registerPharmacy,
  signInTo,
} from './compliance';

// Assigning a course (#37) against the AppHost, with no route mocked. Through the host's API, the realm's local user
// editor publishes a course and the developer registers a pharmacy they manage, of which they are the only member;
// Compliance learns of both through integration events, so the test waits until the catalog lists the course and
// Compliance lists the developer as a member. On the page, the developer chooses that pharmacy in the shell and assigns
// the course to themself; assigning it again names them as already assigned. Keycloak's login is English.

/** The wizard's step, by its heading, which takes the focus on entering it. */
const stepRegion = (page: Page, title: string) => page.getByRole('region', { name: title });

/** Goes through the wizard: the course, the developer, the due date and maybe a yearly repetition; then Zuweisen. */
async function assign(page: Page, course: string, options: { yearly: boolean }): Promise<Locator> {
  const steps = page.getByRole('list', { name: 'Schritte' });
  const next = page.getByRole('button', { name: 'Weiter', exact: true });

  const courseStep = stepRegion(page, 'Schulung wählen');
  await expect(steps.locator('[aria-current="step"]')).toContainText('Schulung');
  const choose = courseStep.getByRole('button', { name: `${course} wählen` });
  await choose.click();
  await expect(choose).toHaveAttribute('aria-pressed', 'true');
  await next.click();

  const learners = stepRegion(page, 'Teilnehmende wählen');
  await expect(learners.getByRole('heading', { name: 'Teilnehmende wählen', level: 2 })).toBeFocused();
  await expect(steps.locator('[aria-current="step"]')).toContainText('Teilnehmende');
  await learners.getByRole('checkbox', { name: `${developer} auswählen` }).check();
  await expect(learners.getByRole('status')).toHaveText('Eine Person ausgewählt');
  await next.click();

  const due = stepRegion(page, 'Fälligkeit festlegen');
  await expect(due.getByRole('heading', { name: 'Fälligkeit festlegen', level: 2 })).toBeFocused();
  await due.getByLabel('Fällig am').fill(daysAhead(30));
  if (options.yearly) {
    await due.getByRole('checkbox', { name: 'Jährlich wiederholen' }).check();
  }
  await next.click();

  const review = stepRegion(page, 'Zuweisung prüfen');
  await expect(review.getByRole('heading', { name: 'Zuweisung prüfen', level: 2 })).toBeFocused();
  await expect(review.getByRole('definition')).toHaveText([
    course,
    developer,
    /\d{4}$/,
    options.yearly ? 'Jährlich' : 'Keine',
  ]);
  await review.getByRole('button', { name: 'Zuweisen', exact: true }).click();

  const result = stepRegion(page, 'Ergebnis');
  await expect(result.getByRole('heading', { name: 'Ergebnis', level: 2 })).toBeFocused();
  return result;
}

test('the developer assigns a published course to themself, and assigning it again names them as already assigned', async ({
  page,
}) => {
  const course = courseTitle();
  const pharmacy = pharmacyName('Apotheke');
  await publishCourse(page.request, course);
  await registerPharmacy(page.request, pharmacy);
  await signInTo(page, pharmacy);

  await page.goto('/compliance/assign');
  await expect(page.getByRole('heading', { name: 'Schulung zuweisen', level: 1 })).toBeVisible();

  const first = await assign(page, course, { yearly: false });
  await expect(first.getByText('Die Schulung wurde einer Person zugewiesen.', { exact: true })).toBeVisible();
  await expect(first.getByRole('list', { name: 'Nicht zugewiesen' })).toHaveCount(0);

  await first.getByRole('button', { name: 'Weitere Schulung zuweisen' }).click();
  await expect(page.getByRole('heading', { name: 'Schulung wählen', level: 2 })).toBeFocused();

  const again = await assign(page, course, { yearly: false });
  await expect(again.getByText('Die Schulung wurde niemandem zugewiesen.', { exact: true })).toBeVisible();
  await expect(again.getByRole('list', { name: 'Nicht zugewiesen' }).getByRole('listitem')).toHaveText([
    `${developer}: Diese Schulung ist bereits zugewiesen.`,
  ]);
});

test.describe('on a tablet held upright', () => {
  // The desktop sidebar's breakpoint (docs/domain/README.md, "Geräte").
  test.use({ viewport: { width: 768, height: 1024 } });

  test('the developer assigns a published course to themself, repeated yearly', async ({ page }) => {
    const course = courseTitle();
    const pharmacy = pharmacyName('Tablet');
    await publishCourse(page.request, course);
    await registerPharmacy(page.request, pharmacy);
    await signInTo(page, pharmacy);

    await page.goto('/compliance/assign');
    await expect(page.getByRole('heading', { name: 'Schulung zuweisen', level: 1 })).toBeVisible();

    const result = await assign(page, course, { yearly: true });
    await expect(result.getByText('Die Schulung wurde einer Person zugewiesen.', { exact: true })).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth)).toBe(
      false,
    );
  });
});
