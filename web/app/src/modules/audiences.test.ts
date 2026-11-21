import { expect, test } from 'vitest';
import { moduleRoutes } from '@/modules';

// Who each module's page is for (docs/domain/README.md, "Vue-Seiten"), as each module's routes.ts declares it.
test("every module's route declares the audience its page is for", () => {
  expect(Object.fromEntries(moduleRoutes.map((route) => [route.path, route.audience]))).toEqual({
    assessment: 'employee',
    catalog: 'manager',
    certification: 'employee',
    compliance: 'employee',
    editorial: 'editor',
    learning: 'employee',
    organisation: 'manager',
  });
});

// A page below a module (its routes.ts's pages) declares its own audience: registering a pharmacy needs no pharmacy.
test('every page below a module declares the audience it is for, and its title', () => {
  expect(
    moduleRoutes.flatMap((route) =>
      (route.pages ?? []).map((page) => ({
        address: `/${route.path}/${page.path}`,
        title: page.title,
        audience: page.audience,
      })),
    ),
  ).toEqual([{ address: '/organisation/register', title: 'Register a pharmacy', audience: 'signed-in' }]);
});
