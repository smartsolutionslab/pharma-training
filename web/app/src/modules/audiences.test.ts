import { expect, test } from 'vitest';
import { setLanguage, t } from '@/i18n';
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

// A page's title is a key into the catalogs (modules.<module>.title), so the sidebar, the breadcrumbs and the palette
// name it in the language shown.
test("every module's route names its title by its key in the catalogs", () => {
  expect(Object.fromEntries(moduleRoutes.map((route) => [route.path, route.titleKey]))).toEqual({
    assessment: 'modules.assessment.title',
    catalog: 'modules.catalog.title',
    certification: 'modules.certification.title',
    compliance: 'modules.compliance.title',
    editorial: 'modules.editorial.title',
    learning: 'modules.learning.title',
    organisation: 'modules.organisation.title',
  });
});

test("every module's title is in the catalogs, in German and in English", () => {
  const titles = Object.fromEntries(moduleRoutes.map((route) => [route.path, t(route.titleKey)]));
  setLanguage('en-GB', { remember: false });
  const english = Object.fromEntries(moduleRoutes.map((route) => [route.path, t(route.titleKey)]));

  expect(titles).toEqual({
    assessment: 'Tests',
    catalog: 'Schulungskatalog',
    certification: 'Teilnahmebestätigungen',
    compliance: 'Zuweisungen',
    editorial: 'Redaktion',
    learning: 'Meine Schulungen',
    organisation: 'Mitglieder',
  });
  expect(english).toEqual({
    assessment: 'Tests',
    catalog: 'Course catalogue',
    certification: 'Certificates',
    compliance: 'Assignments',
    editorial: 'Editorial',
    learning: 'My courses',
    organisation: 'Members',
  });
});

// A page below a module (its routes.ts's pages) declares its own audience: registering a pharmacy needs no pharmacy.
test('every page below a module declares the audience it is for, and its title by key', () => {
  expect(
    moduleRoutes.flatMap((route) =>
      (route.pages ?? []).map((page) => ({
        address: `/${route.path}/${page.path}`,
        titleKey: page.titleKey,
        audience: page.audience,
      })),
    ),
  ).toEqual([
    {
      address: '/organisation/register',
      titleKey: 'modules.organisation.pages.register.title',
      audience: 'signed-in',
    },
  ]);
  expect(t('modules.organisation.pages.register.title')).toBe('Apotheke registrieren');
});

// The router maps a page at /<module>/<page>, and the audience gate and the breadcrumbs find it by the address's second
// segment: a page's path is that one segment, never empty and never two.
test('every page below a module has a path of one segment', () => {
  const paths = moduleRoutes.flatMap((route) => (route.pages ?? []).map((page) => page.path));
  expect(paths.length).toBeGreaterThan(0);
  for (const path of paths) {
    expect(path).toMatch(/^[^/]+$/);
  }
});
