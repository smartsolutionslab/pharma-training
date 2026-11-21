import type { Page } from '@playwright/test';

/**
 * GET /organisation/pharmacies/mine answers one membership the user manages, whatever the database holds: a pharmacy's
 * pages are then open to the developer, whatever pharmacies register-pharmacy.spec.ts has made them a member of, and
 * the only membership is current without a choice.
 */
export async function memberOfOnePharmacy(page: Page) {
  const pharmacy = {
    id: '0198c1a0-0000-7000-8000-0000000000a1',
    name: 'Apotheke am Markt',
    membershipId: '0198c1a0-0000-7000-8000-0000000000b1',
    role: 'Manager',
    jobFunction: 'Pharmacist',
    canManageMembers: true,
    canAssignCourses: true,
  };
  await page.route(/\/api\/organisation\/pharmacies\/mine(\?|$)/, (route) =>
    route.fulfill({ json: { items: [pharmacy], totalCount: 1, page: 1, pageSize: 100 } }),
  );
}
