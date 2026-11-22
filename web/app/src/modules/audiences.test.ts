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

// A detail page below a module (its routes.ts's details) has a parameter in its address, `versions/:id`, and is listed
// in neither the sidebar nor the palette; it declares its own audience and its title by key, which the breadcrumbs name.
test('every detail below a module declares its audience and its title by key', () => {
  expect(
    moduleRoutes.flatMap((route) =>
      (route.details ?? []).map((detail) => ({
        address: `/${route.path}/${detail.path}`,
        titleKey: detail.titleKey,
        audience: detail.audience,
      })),
    ),
  ).toEqual([
    {
      address: '/editorial/versions/:id',
      titleKey: 'modules.editorial.details.version.title',
      audience: 'editor',
    },
  ]);
  expect(t('modules.editorial.details.version.title')).toBe('Schulungsversion');
  setLanguage('en-GB', { remember: false });
  expect(t('modules.editorial.details.version.title')).toBe('Course version');
});

// An entry page (its routes.ts's entries) is a page a link from outside the app opens, at an address of its own outside
// its module's: the invitation mail's link. It is for every signed-in user, with or without a pharmacy, and is named by
// its title in the breadcrumbs, in both languages.
test('every entry page declares its absolute address, the audience it is for, and its title by key', () => {
  expect(
    moduleRoutes.flatMap((route) =>
      (route.entries ?? []).map((entry) => ({
        module: route.path,
        address: entry.path,
        titleKey: entry.titleKey,
        audience: entry.audience,
      })),
    ),
  ).toEqual([
    {
      module: 'organisation',
      address: '/invitations/accept',
      titleKey: 'modules.organisation.entries.acceptInvitation.title',
      audience: 'signed-in',
    },
  ]);
});

test("every entry page's title is in the catalogs, in German and in English", () => {
  const keys = moduleRoutes.flatMap((route) => (route.entries ?? []).map((entry) => entry.titleKey));
  expect(keys.map((key) => t(key))).toEqual(['Einladung annehmen']);
  setLanguage('en-GB', { remember: false });
  expect(keys.map((key) => t(key))).toEqual(['Accept an invitation']);
});

// An entry page is at an address of its own, outside every module's: under a module's address the audience gate and
// the breadcrumbs would take it for one of that module's pages.
test("no entry page's address starts with a module's own path", () => {
  const modulePaths = new Set(moduleRoutes.map((route) => route.path));
  const entries = moduleRoutes.flatMap((route) => (route.entries ?? []).map((entry) => entry.path));

  expect(entries.length).toBeGreaterThan(0);
  expect(entries.filter((path) => modulePaths.has(path.split('/')[1] ?? ''))).toEqual([]);
});
