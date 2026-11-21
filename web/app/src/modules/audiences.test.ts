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
