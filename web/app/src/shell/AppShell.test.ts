import { cleanup, render, screen, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import type { User } from 'oidc-client-ts';
import { createPinia } from 'pinia';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import { computed, type Plugin } from 'vue';
import { createMemoryHistory } from 'vue-router';
import type { MyPharmacyDto, PagedResultOfMyPharmacyDto } from '@/api/generated';
import App from '@/App.vue';
import { type Auth, authKey } from '@/auth/useAuth';
import { moduleRoutes } from '@/modules';
import { createAppRouter } from '@/router';
import { i18n, setLanguage, t } from '@/i18n';

// The user menu has its own test; here it stands in, so this test checks the shell around it.
vi.mock('@/auth/UserMenu.vue', async () => {
  const { h } = await import('vue');
  return { default: { render: () => h('p', 'The user menu') } };
});

// Each module's page, and each page below it, stands in as its title in the language shown, as a module's page heads
// itself with t(route.titleKey), and beside the app's modules is one of the tests' own, with a page below it for every
// signed-in user and one for editors: a real module's page needs the host, which these tests do not have. The audiences
// are the modules' own.
vi.mock('@/modules', async (importOriginal) => {
  const { h } = await import('vue');
  const { t } = await import('@/i18n');
  const modules = await importOriginal<typeof import('@/modules')>();
  const page = (titleKey: string) => ({ render: () => h('main', [h('h1', t(titleKey))]) });
  const reports = {
    path: 'reports',
    titleKey: 'modules.reports.title',
    audience: 'employee' as const,
    component: page('modules.reports.title'),
    pages: [
      {
        path: 'new',
        titleKey: 'modules.reports.pages.new.title',
        audience: 'signed-in' as const,
        component: page('modules.reports.pages.new.title'),
      },
      {
        path: 'review',
        titleKey: 'modules.reports.pages.review.title',
        audience: 'editor' as const,
        component: page('modules.reports.pages.review.title'),
      },
    ],
    // A detail page with a parameter, for editors though its module is for employees.
    details: [
      {
        path: 'entries/:id',
        titleKey: 'modules.reports.details.entry.title',
        audience: 'editor' as const,
        component: page('modules.reports.details.entry.title'),
      },
    ],
  };
  return {
    ...modules,
    moduleRoutes: [
      ...modules.moduleRoutes.map((route) => ({
        ...route,
        component: page(route.titleKey),
        pages: route.pages?.map((below) => ({ ...below, component: page(below.titleKey) })),
        details: route.details?.map((detail) => ({ ...detail, component: page(detail.titleKey) })),
      })),
      reports,
    ],
  };
});

// The tests' own module is in neither catalog: its titles are added for this file, in both languages, under the keys
// its route names.
i18n.global.mergeLocaleMessage('de-DE', {
  modules: {
    reports: {
      title: 'Berichte',
      pages: { new: { title: 'Neuer Bericht' }, review: { title: 'Berichte prüfen' } },
      details: { entry: { title: 'Berichtseintrag' } },
    },
  },
});
i18n.global.mergeLocaleMessage('en-GB', {
  modules: {
    reports: {
      title: 'Reports',
      pages: { new: { title: 'New report' }, review: { title: 'Review reports' } },
      details: { entry: { title: 'Report entry' } },
    },
  },
});

/** The page titles the shell shows, in the language shown. */
const titles = (keys: string[]) => keys.map((key) => t(key));
const home = () => t('shell.home');
const organisation = () => t('modules.organisation.title');
const learning = () => t('modules.learning.title');
const editorial = () => t('modules.editorial.title');
const reportsTitle = () => t('modules.reports.title');
const newReport = () => t('modules.reports.pages.new.title');
const reviewReports = () => t('modules.reports.pages.review.title');
const registerPage = () => t('modules.organisation.pages.register.title');
const palette = () => t('shell.palette.title');
const courseVersion = () => t('modules.editorial.details.version.title');
const reportEntry = () => t('modules.reports.details.entry.title');

// GET /organisation/pharmacies/mine as useMyPharmacies answers it: TanStack Query's refs, which a test sets.
const pharmacies = await vi.hoisted(async () => {
  const { ref } = await import('vue');
  return {
    data: ref<PagedResultOfMyPharmacyDto | undefined>(undefined),
    isPending: ref(true),
    isError: ref(false),
    refetch: vi.fn(),
  };
});
vi.mock('@/shell/useMyPharmacies', () => ({ useMyPharmacies: () => pharmacies }));

const markt: MyPharmacyDto = {
  id: '0198c1a0-0000-7000-8000-0000000000a1',
  name: 'Apotheke am Markt',
  membershipId: '0198c1a0-0000-7000-8000-0000000000b1',
  role: 'Manager',
  jobFunction: 'Pharmacist',
  canManageMembers: true,
  canAssignCourses: true,
};
const dom: MyPharmacyDto = {
  id: '0198c1a0-0000-7000-8000-0000000000a2',
  name: 'Apotheke am Dom',
  membershipId: '0198c1a0-0000-7000-8000-0000000000b2',
  role: 'Employee',
  jobFunction: 'Pta',
  canManageMembers: false,
  canAssignCourses: false,
};

function answer(items: MyPharmacyDto[]) {
  pharmacies.data.value = { items, totalCount: items.length, page: 1, pageSize: 100 };
  pharmacies.isPending.value = false;
  pharmacies.isError.value = false;
}

function loading() {
  pharmacies.data.value = undefined;
  pharmacies.isPending.value = true;
  pharmacies.isError.value = false;
}

beforeEach(() => {
  localStorage.clear();
  loading();
});
afterEach(() => {
  localStorage.clear();
});

/** A signed-in user whose ID token holds these realm roles. */
function signedInWith(roles: string[]): Plugin {
  const auth = {
    user: computed(() => ({ profile: { preferred_username: 'someone', realm_access: { roles } } }) as unknown as User),
    isAuthenticated: computed(() => true),
  } as unknown as Auth;
  return { install: (app) => app.provide(authKey, auth) };
}

/**
 * The shell at an address, for a user with these realm roles; unless a test says otherwise, an editor who manages
 * one pharmacy, who sees every page.
 */
async function renderAt(path: string, { roles = ['editor'] }: { roles?: string[] } = {}) {
  if (pharmacies.isPending.value) {
    answer([markt]);
  }
  return renderLoadingAt(path, roles);
}

/** The shell as renderAt renders it, with the memberships as the test left them, loading included. */
async function renderLoadingAt(path: string, roles: string[] = ['editor']) {
  const router = createAppRouter(createMemoryHistory());
  await router.push(path);
  render(App, { global: { plugins: [router, createPinia(), signedInWith(roles)] } });
  return { router };
}

const sidebarLinks = () =>
  within(screen.getByRole('navigation', { name: t('shell.navigation.label') }))
    .getAllByRole('link')
    .map((link) => link.textContent.trim());

test('the sidebar lists the start page and every module the user may see, from their routes', async () => {
  await renderAt('/');

  const navigation = within(screen.getByRole('navigation', { name: t('shell.navigation.label') }));
  expect(sidebarLinks()).toEqual([home(), ...moduleRoutes.map((module) => t(module.titleKey))]);
  expect(navigation.getByRole('link', { name: home() })).toHaveAttribute('aria-current', 'page');
});

// The pages below the modules each user may see, in the command palette after the modules: registering a pharmacy and
// the tests' new report are for every signed-in user, the tests' review for editors, whatever their module's audience.
const forEveryone = ['modules.organisation.pages.register.title', 'modules.reports.pages.new.title'];
const forEditors = [...forEveryone, 'modules.reports.pages.review.title'];

const audiences = [
  {
    who: 'an employee',
    roles: [],
    memberships: [dom],
    pages: [
      'shell.home',
      'modules.assessment.title',
      'modules.certification.title',
      'modules.compliance.title',
      'modules.learning.title',
      'modules.reports.title',
    ],
    below: forEveryone,
  },
  {
    who: 'a member who may manage the pharmacy',
    roles: [],
    memberships: [markt],
    pages: [
      'shell.home',
      'modules.assessment.title',
      'modules.catalog.title',
      'modules.certification.title',
      'modules.compliance.title',
      'modules.learning.title',
      'modules.organisation.title',
      'modules.reports.title',
    ],
    below: forEveryone,
  },
  {
    who: 'an editor of no pharmacy',
    roles: ['editor'],
    memberships: [],
    pages: ['shell.home', 'modules.editorial.title'],
    below: forEditors,
  },
  {
    who: 'an editor who is an employee',
    roles: ['editor'],
    memberships: [dom],
    pages: [
      'shell.home',
      'modules.assessment.title',
      'modules.certification.title',
      'modules.compliance.title',
      'modules.editorial.title',
      'modules.learning.title',
      'modules.reports.title',
    ],
    below: forEditors,
  },
  { who: 'a user of no pharmacy and no role', roles: [], memberships: [], pages: ['shell.home'], below: forEveryone },
];

test.each(audiences)('the sidebar shows $who the pages for them', async ({ roles, memberships, pages }) => {
  answer(memberships);

  await renderAt('/', { roles });

  expect(sidebarLinks()).toEqual(titles(pages));
});

// The sidebar lists the modules only: a page below a module is reached from the palette, the breadcrumbs or a link.
test.each(audiences)(
  'the command palette offers $who the pages for them, the pages below the modules after the modules',
  async ({ roles, memberships, pages, below }) => {
    const user = userEvent.setup();
    answer(memberships);
    await renderAt('/', { roles });

    await user.keyboard('{Control>}k{/Control}');
    const dialog = await screen.findByRole('dialog', { name: palette() });

    expect(
      within(dialog)
        .getAllByRole('option')
        .map((option) => option.textContent.trim()),
    ).toEqual(titles([...pages, ...below]));
  },
);

test('the start page is there for a user with no pharmacy and no role', async () => {
  answer([]);

  await renderAt('/', { roles: [] });

  expect(sidebarLinks()).toEqual([home()]);
  expect(screen.getByRole('heading', { name: t('app.name'), level: 1 })).toBeInTheDocument();
});

test("an employee who opens a manager's page is told it is not available, and led home", async () => {
  answer([dom]);

  await renderAt('/organisation', { roles: [] });

  const main = within(await screen.findByRole('main'));
  expect(main.getByRole('heading', { name: t('shell.gate.notAvailable'), level: 1 })).toBeInTheDocument();
  expect(main.getByText(t('shell.gate.notInPharmacy', { name: 'Apotheke am Dom' }))).toBeInTheDocument();
  expect(main.getByRole('link', { name: t('shell.gate.goHome') })).toHaveAttribute('href', '/');
  expect(screen.queryByRole('heading', { name: organisation() })).not.toBeInTheDocument();
});

test('with several pharmacies and none chosen a pharmacy page asks the user to choose one', async () => {
  answer([markt, dom]);

  await renderAt('/learning', { roles: [] });

  const main = within(await screen.findByRole('main'));
  expect(main.getByRole('heading', { name: t('shell.gate.notAvailable'), level: 1 })).toBeInTheDocument();
  expect(main.getByText(t('shell.gate.choosePharmacy'))).toBeInTheDocument();
  expect(screen.queryByRole('heading', { name: learning() })).not.toBeInTheDocument();
});

test('a user of no pharmacy who opens a pharmacy page is told so', async () => {
  answer([]);

  await renderAt('/learning', { roles: [] });

  const main = within(await screen.findByRole('main'));
  expect(main.getByRole('heading', { name: t('shell.gate.notAvailable'), level: 1 })).toBeInTheDocument();
  expect(main.getByText(t('shell.gate.noPharmacy'))).toBeInTheDocument();
});

test('an editor of no pharmacy reaches the editorial page', async () => {
  answer([]);

  await renderAt('/editorial', { roles: ['editor'] });

  expect(await screen.findByRole('heading', { name: editorial(), level: 1 })).toBeInTheDocument();
  expect(screen.queryByRole('heading', { name: t('shell.gate.notAvailable') })).not.toBeInTheDocument();
  expect(sidebarLinks()).toEqual([home(), editorial()]);
});

test("switching from a pharmacy the user manages to one they work in closes the manager's page", async () => {
  localStorage.setItem('pharmacy', markt.id);
  answer([markt, dom]);
  const user = userEvent.setup();
  await renderAt('/organisation', { roles: [] });
  expect(await screen.findByRole('heading', { name: organisation(), level: 1 })).toBeInTheDocument();

  await user.click(screen.getByRole('button', { name: t('shell.pharmacy.current', { name: 'Apotheke am Markt' }) }));
  const menu = await screen.findByRole('menu', { name: t('shell.pharmacy.list') });
  await user.click(within(menu).getByRole('menuitemradio', { name: /Apotheke am Dom/ }));

  expect(await screen.findByRole('heading', { name: t('shell.gate.notAvailable'), level: 1 })).toBeInTheDocument();
  expect(screen.getByText(t('shell.gate.notInPharmacy', { name: 'Apotheke am Dom' }))).toBeInTheDocument();
  expect(screen.queryByRole('heading', { name: organisation(), level: 1 })).not.toBeInTheDocument();
  expect(sidebarLinks()).not.toContain(organisation());
});

// Neither the page nor "Not available" flashes before the memberships are known: the page area shows a skeleton.
test("while the memberships load a pharmacy page shows a skeleton, then the page once it is the user's", async () => {
  await renderLoadingAt('/organisation', []);

  const pageArea = () => document.querySelector('[data-slot="sidebar-inset"]');
  expect(pageArea()?.querySelector('[data-slot="skeleton"]')).not.toBeNull();
  // The page's landmark is there while it loads, busy, and says so to a screen reader.
  const busy = screen.getByRole('main');
  expect(busy).toHaveAttribute('aria-busy', 'true');
  expect(busy.querySelector('[data-slot="skeleton"]')).not.toBeNull();
  expect(within(busy).getByText(t('shell.gate.loading'))).toHaveClass('sr-only');
  expect(screen.queryByRole('heading', { name: organisation() })).not.toBeInTheDocument();
  expect(screen.queryByRole('heading', { name: t('shell.gate.notAvailable') })).not.toBeInTheDocument();

  answer([markt]);

  expect(await screen.findByRole('heading', { name: organisation(), level: 1 })).toBeInTheDocument();
  expect(pageArea()?.querySelector('[data-slot="skeleton"]')).toBeNull();
});

test('a signed-in user who is no editor is told the editorial page is not available to them', async () => {
  answer([markt]);

  await renderAt('/editorial', { roles: [] });

  const main = within(await screen.findByRole('main'));
  expect(main.getByRole('heading', { name: t('shell.gate.notAvailable'), level: 1 })).toBeInTheDocument();
  expect(main.getByText(t('shell.gate.notForYou'))).toBeInTheDocument();
  expect(main.getByRole('link', { name: t('shell.gate.goHome') })).toHaveAttribute('href', '/');
  expect(screen.queryByRole('heading', { name: editorial() })).not.toBeInTheDocument();
});

test('when the memberships could not be loaded a pharmacy page says so and tries again on request', async () => {
  const user = userEvent.setup();
  pharmacies.data.value = undefined;
  pharmacies.isPending.value = false;
  pharmacies.isError.value = true;
  pharmacies.refetch.mockClear();

  await renderLoadingAt('/learning', []);

  const main = within(await screen.findByRole('main'));
  expect(main.getByRole('heading', { name: t('shell.gate.notAvailable'), level: 1 })).toBeInTheDocument();
  expect(main.getByText(t('shell.gate.pharmaciesFailed'))).toBeInTheDocument();
  expect(screen.queryByRole('heading', { name: learning() })).not.toBeInTheDocument();

  await user.click(main.getByRole('button', { name: t('shell.retry') }));

  expect(pharmacies.refetch).toHaveBeenCalledOnce();
});

// TanStack Query keeps the data it had when a refetch in the background fails; the page the user is on stays.
test('a failed refetch that keeps the memberships leaves the page the user is on', async () => {
  const user = userEvent.setup();
  answer([dom]);
  await renderAt('/learning', { roles: [] });
  expect(await screen.findByRole('heading', { name: learning(), level: 1 })).toBeInTheDocument();

  pharmacies.isError.value = true;
  // The shell renders again once the palette opens: by then a shell that blanked itself would have done so.
  await user.keyboard('{Control>}k{/Control}');
  const dialog = await screen.findByRole('dialog', { name: palette() });

  expect(within(dialog).getByRole('option', { name: learning() })).toBeInTheDocument();
  await user.keyboard('{Escape}');
  await vi.waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  expect(screen.getByRole('heading', { name: learning(), level: 1 })).toBeInTheDocument();
  expect(screen.queryByRole('heading', { name: t('shell.gate.notAvailable') })).not.toBeInTheDocument();
});

// A module the user may not see is an unknown segment of the address to the breadcrumbs: they do not name it.
test('the breadcrumbs show a module the user may not see as its segment of the address', async () => {
  answer([dom]);

  await renderAt('/organisation/members', { roles: [] });

  const breadcrumbs = within(screen.getByRole('navigation', { name: t('ui.breadcrumb.label') }));
  expect(breadcrumbs.getByRole('link', { name: 'organisation' })).toHaveAttribute('href', '/organisation');
  expect(breadcrumbs.queryByText(organisation())).not.toBeInTheDocument();
  expect(breadcrumbs.getByText('members')).toHaveAttribute('aria-current', 'page');
});

test('the sidebar collapses from its trigger and opens again', async () => {
  const user = userEvent.setup();
  await renderAt('/');
  const sidebar = () => document.querySelector('[data-slot="sidebar"]');
  expect(sidebar()).toHaveAttribute('data-state', 'expanded');

  await user.click(within(screen.getByRole('banner')).getByRole('button', { name: t('ui.sidebar.toggle') }));
  await vi.waitFor(() => expect(sidebar()).toHaveAttribute('data-state', 'collapsed'));

  await user.click(within(screen.getByRole('banner')).getByRole('button', { name: t('ui.sidebar.toggle') }));
  await vi.waitFor(() => expect(sidebar()).toHaveAttribute('data-state', 'expanded'));
});

test('on the start page the breadcrumbs name only it', async () => {
  await renderAt('/');

  const breadcrumbs = within(screen.getByRole('navigation', { name: t('ui.breadcrumb.label') }));
  expect(breadcrumbs.getByText(home())).toHaveAttribute('aria-current', 'page');
  expect(breadcrumbs.getByText(home())).not.toHaveAttribute('href');
});

// The current page is text, not a disabled link: the sidebar's link to it stays the only link of that name.
test('the page the user is on is text in the breadcrumbs, not a second link to it', async () => {
  await renderAt('/reports');

  const breadcrumbs = within(screen.getByRole('navigation', { name: t('ui.breadcrumb.label') }));
  expect(breadcrumbs.queryByRole('link', { name: reportsTitle() })).not.toBeInTheDocument();
  expect(breadcrumbs.getByText(reportsTitle())).toHaveAttribute('aria-current', 'page');
  expect(screen.getAllByRole('link', { name: reportsTitle() })).toHaveLength(1);
});

test("the breadcrumbs lead from the start page to a module's page and below it", async () => {
  await renderAt('/reports/details');

  const breadcrumbs = within(screen.getByRole('navigation', { name: t('ui.breadcrumb.label') }));
  expect(breadcrumbs.getByRole('link', { name: home() })).toHaveAttribute('href', '/');
  expect(breadcrumbs.getByRole('link', { name: reportsTitle() })).toHaveAttribute('href', '/reports');
  expect(breadcrumbs.getByText('details')).toHaveAttribute('aria-current', 'page');
});

test.each([
  ['Ctrl+K', '{Control>}k{/Control}'],
  ['Cmd+K', '{Meta>}k{/Meta}'],
])('%s opens the command palette and Escape closes it', async (_, keys) => {
  const user = userEvent.setup();
  await renderAt('/');

  await user.keyboard(keys);
  const dialog = await screen.findByRole('dialog', { name: palette() });
  await vi.waitFor(() => expect(within(dialog).getByRole('textbox', { name: palette() })).toHaveFocus());
  expect(
    within(dialog)
      .getAllByRole('option')
      .map((option) => option.textContent.trim()),
  ).toEqual([home(), ...moduleRoutes.map((module) => t(module.titleKey)), ...titles(forEditors)]);

  await user.keyboard('{Escape}');
  await vi.waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
});

test('the keyboard alone goes to a page from the command palette', async () => {
  const user = userEvent.setup();
  const { router } = await renderAt('/');

  await user.keyboard('{Control>}k{/Control}');
  await screen.findByRole('dialog', { name: palette() });
  await user.keyboard('{ArrowDown}'.repeat(moduleRoutes.length));
  await vi.waitFor(() =>
    expect(screen.getByRole('option', { name: reportsTitle() })).toHaveAttribute('data-highlighted'),
  );
  await user.keyboard('{Enter}');

  await vi.waitFor(() => expect(router.currentRoute.value.path).toBe('/reports'));
  await vi.waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  expect(await screen.findByRole('heading', { name: reportsTitle() })).toBeInTheDocument();
});

test('typing narrows the command palette to the pages that match', async () => {
  const user = userEvent.setup();
  await renderAt('/');

  await user.keyboard('{Control>}k{/Control}');
  await screen.findByRole('dialog', { name: palette() });
  await user.keyboard('no such page');

  await vi.waitFor(() => expect(screen.queryAllByRole('option')).toHaveLength(0));
  expect(screen.getByText(t('shell.palette.empty'))).toBeInTheDocument();
});

test('the command palette goes to a page below a module', async () => {
  const user = userEvent.setup();
  const { router } = await renderAt('/');

  await user.keyboard('{Control>}k{/Control}');
  const dialog = await screen.findByRole('dialog', { name: palette() });
  await user.click(within(dialog).getByRole('option', { name: newReport() }));

  await vi.waitFor(() => expect(router.currentRoute.value.path).toBe('/reports/new'));
  expect(await screen.findByRole('heading', { name: newReport(), level: 1 })).toBeInTheDocument();
});

// A page below a module is governed by its own audience, not its module's: registering a pharmacy needs no pharmacy.
test('a page for every signed-in user is there for a user of no pharmacy and no role', async () => {
  answer([]);

  await renderAt('/reports/new', { roles: [] });

  expect(await screen.findByRole('heading', { name: newReport(), level: 1 })).toBeInTheDocument();
  expect(screen.queryByRole('heading', { name: t('shell.gate.notAvailable') })).not.toBeInTheDocument();
  expect(sidebarLinks()).toEqual([home()]);
});

test('the page to register a pharmacy is there for a user of no pharmacy and no role', async () => {
  answer([]);

  await renderAt('/organisation/register', { roles: [] });

  expect(await screen.findByRole('heading', { name: registerPage(), level: 1 })).toBeInTheDocument();
  expect(screen.queryByRole('heading', { name: t('shell.gate.notAvailable') })).not.toBeInTheDocument();
});

// Neither a skeleton nor "Not available": the page is the user's whatever their memberships turn out to be.
test('while the memberships load a page for every signed-in user shows at once, without a skeleton', async () => {
  await renderLoadingAt('/organisation/register', []);

  expect(screen.getByRole('heading', { name: registerPage(), level: 1 })).toBeInTheDocument();
  expect(document.querySelector('[data-slot="sidebar-inset"] [data-slot="skeleton"]')).toBeNull();
  expect(screen.getByRole('main')).not.toHaveAttribute('aria-busy');
  expect(screen.queryByRole('heading', { name: t('shell.gate.notAvailable') })).not.toBeInTheDocument();
});

test('a page below a module for editors is refused to a user who is no editor, though the module is theirs', async () => {
  answer([dom]);

  await renderAt('/reports/review', { roles: [] });

  const main = within(await screen.findByRole('main'));
  expect(main.getByRole('heading', { name: t('shell.gate.notAvailable'), level: 1 })).toBeInTheDocument();
  expect(main.getByText(t('shell.gate.notForYou'))).toBeInTheDocument();
  expect(screen.queryByRole('heading', { name: reviewReports() })).not.toBeInTheDocument();
});

test('a page below a module for editors is there for an editor of no pharmacy', async () => {
  answer([]);

  await renderAt('/reports/review', { roles: ['editor'] });

  expect(await screen.findByRole('heading', { name: reviewReports(), level: 1 })).toBeInTheDocument();
  expect(screen.queryByRole('heading', { name: t('shell.gate.notAvailable') })).not.toBeInTheDocument();
});

test("the breadcrumbs name a page below a module by its title, after the module's", async () => {
  await renderAt('/organisation/register');

  const breadcrumbs = within(screen.getByRole('navigation', { name: t('ui.breadcrumb.label') }));
  expect(breadcrumbs.getByRole('link', { name: home() })).toHaveAttribute('href', '/');
  expect(breadcrumbs.getByRole('link', { name: organisation() })).toHaveAttribute('href', '/organisation');
  expect(breadcrumbs.getByText(registerPage())).toHaveAttribute('aria-current', 'page');
});

// The module of a page the user may see is named by its title, but it is no link: it would lead to a page refused to
// them ("Not available").
test('the breadcrumbs name a page the user may see by its title, and its module as text, below a module they may not', async () => {
  answer([]);

  await renderAt('/organisation/register', { roles: [] });

  const breadcrumbs = within(screen.getByRole('navigation', { name: t('ui.breadcrumb.label') }));
  expect(breadcrumbs.queryByRole('link', { name: /organisation/i })).not.toBeInTheDocument();
  const module = breadcrumbs.getByText(organisation());
  expect(module.closest('a')).toBeNull();
  expect(module).not.toHaveAttribute('aria-current');
  expect(breadcrumbs.queryByText('organisation')).not.toBeInTheDocument();
  expect(breadcrumbs.getByText(registerPage())).toHaveAttribute('aria-current', 'page');
});

// A page the user may not see is an unknown segment of the address to the breadcrumbs, as a module is.
test('the breadcrumbs name a page below a module by its title only where the user may see it', async () => {
  answer([dom]);
  const { router } = await renderAt('/reports/review', { roles: [] });

  const breadcrumbs = within(screen.getByRole('navigation', { name: t('ui.breadcrumb.label') }));
  expect(breadcrumbs.getByRole('link', { name: reportsTitle() })).toHaveAttribute('href', '/reports');
  expect(breadcrumbs.getByText('review')).toHaveAttribute('aria-current', 'page');
  expect(breadcrumbs.queryByText(reviewReports())).not.toBeInTheDocument();

  await router.push('/reports/new');

  expect(await breadcrumbs.findByText(newReport())).toHaveAttribute('aria-current', 'page');
});

// The way in for a user of no pharmacy: the switcher in the sidebar leads to the page that registers one.
test("a user of no pharmacy follows the switcher's link to the page that registers one", async () => {
  const user = userEvent.setup();
  answer([]);
  const { router } = await renderAt('/', { roles: [] });

  await user.click(
    within(screen.getByRole('region', { name: t('shell.pharmacy.region') })).getByRole('link', {
      name: t('shell.pharmacy.register'),
    }),
  );

  await vi.waitFor(() => expect(router.currentRoute.value.path).toBe('/organisation/register'));
  expect(await screen.findByRole('heading', { name: registerPage(), level: 1 })).toBeInTheDocument();
});

test('the command palette speaks German by default', async () => {
  const user = userEvent.setup();
  await renderAt('/');

  const opener = screen.getByRole('button', { name: /^Seite suchen…/ });
  expect(opener).toHaveTextContent(t('shell.palette.open'));
  expect(opener).toHaveTextContent(t('shell.palette.shortcut'));
  expect(opener).toHaveTextContent('Strg K');
  await user.click(opener);

  const dialog = await screen.findByRole('dialog', { name: 'Seite öffnen' });
  expect(dialog).toHaveAccessibleDescription(t('shell.palette.description'));
  expect(dialog).toHaveAccessibleDescription('Wählen Sie die Seite, die Sie öffnen möchten.');
  const input = within(dialog).getByRole('textbox', { name: t('shell.palette.title') });
  expect(input).toHaveAttribute('placeholder', t('shell.palette.open'));
  const list = within(dialog).getByRole('listbox', { name: t('shell.palette.pages') });
  expect(within(list).getByText(t('shell.palette.pages'))).toBeInTheDocument();
  expect(within(list).getByRole('option', { name: 'Startseite' })).toBeInTheDocument();
  await user.keyboard('keine solche Seite');
  expect(await screen.findByText('Keine Seite gefunden.')).toBeInTheDocument();
});

test('the sidebar and the breadcrumbs name the pages in German by default', async () => {
  await renderAt('/organisation/register');

  const navigation = screen.getByRole('navigation', { name: 'Bereiche' });
  expect(
    within(navigation)
      .getAllByRole('link')
      .map((link) => link.textContent.trim()),
  ).toEqual([
    'Startseite',
    'Tests',
    'Schulungskatalog',
    'Teilnahmebestätigungen',
    'Zuweisungen',
    'Redaktion',
    'Meine Schulungen',
    'Mitglieder',
    'Berichte',
  ]);
  expect(screen.getByText('Bereiche', { selector: '[data-sidebar="group-label"]' })).toBeInTheDocument();
  const breadcrumbs = within(screen.getByRole('navigation', { name: t('ui.breadcrumb.label') }));
  expect(breadcrumbs.getByRole('link', { name: 'Startseite' })).toHaveAttribute('href', '/');
  expect(breadcrumbs.getByRole('link', { name: 'Mitglieder' })).toHaveAttribute('href', '/organisation');
  expect(breadcrumbs.getByText('Apotheke registrieren')).toHaveAttribute('aria-current', 'page');
});

// The titles are keys, translated as the shell renders: switching the language renames every page at once.
test('switching the language renames the pages in the sidebar, the breadcrumbs and the command palette', async () => {
  const user = userEvent.setup();
  await renderAt('/organisation/register');
  const german = sidebarLinks();

  setLanguage('en-GB', { remember: false });

  await vi.waitFor(() =>
    expect(sidebarLinks()).toEqual([
      'Home',
      'Tests',
      'Course catalogue',
      'Certificates',
      'Assignments',
      'Editorial',
      'My courses',
      'Members',
      'Reports',
    ]),
  );
  expect(sidebarLinks()).not.toEqual(german);
  expect(screen.getByRole('navigation', { name: 'Sections' })).toBeInTheDocument();
  const breadcrumbs = within(screen.getByRole('navigation', { name: 'Breadcrumb' }));
  expect(breadcrumbs.getByRole('link', { name: 'Home' })).toHaveAttribute('href', '/');
  expect(breadcrumbs.getByRole('link', { name: 'Members' })).toHaveAttribute('href', '/organisation');
  expect(breadcrumbs.getByText('Register a pharmacy')).toHaveAttribute('aria-current', 'page');

  await user.keyboard('{Control>}k{/Control}');
  const dialog = await screen.findByRole('dialog', { name: 'Open a page' });
  expect(within(dialog).getByRole('option', { name: 'Course catalogue' })).toBeInTheDocument();
  expect(within(dialog).getByRole('option', { name: 'Register a pharmacy' })).toBeInTheDocument();
});

test('a page that is not available says so in German by default, and leads home', async () => {
  answer([dom]);

  await renderAt('/organisation', { roles: [] });

  const main = within(await screen.findByRole('main'));
  expect(main.getByRole('heading', { name: 'Nicht verfügbar', level: 1 })).toBeInTheDocument();
  expect(main.getByText('Diese Seite steht Ihnen in Apotheke am Dom nicht zur Verfügung.')).toBeInTheDocument();
  expect(main.getByRole('link', { name: 'Zur Startseite' })).toHaveAttribute('href', '/');
});

test('when the memberships could not be loaded a pharmacy page says so in German', async () => {
  pharmacies.data.value = undefined;
  pharmacies.isPending.value = false;
  pharmacies.isError.value = true;

  await renderLoadingAt('/learning', []);

  const main = within(await screen.findByRole('main'));
  expect(main.getByText('Ihre Apotheken konnten nicht geladen werden.')).toBeInTheDocument();
  expect(main.getByRole('button', { name: 'Erneut versuchen' })).toBeInTheDocument();
});

test('while the memberships load a pharmacy page says so to a screen reader in German', async () => {
  await renderLoadingAt('/organisation', []);

  expect(within(screen.getByRole('main')).getByText('Wird geladen…')).toHaveClass('sr-only');
});

test('switching the language renames the pharmacy switcher and the reason a page is not available', async () => {
  localStorage.setItem('pharmacy', dom.id);
  answer([markt, dom]);
  await renderAt('/organisation', { roles: [] });
  expect(await screen.findByRole('heading', { name: t('shell.gate.notAvailable'), level: 1 })).toBeInTheDocument();

  setLanguage('en-GB', { remember: false });

  expect(await screen.findByRole('heading', { name: 'Not available', level: 1 })).toBeInTheDocument();
  expect(screen.getByText('This page is not available to you in Apotheke am Dom.')).toBeInTheDocument();
  expect(screen.getByRole('link', { name: 'Back to Home' })).toHaveAttribute('href', '/');
  const pharmacy = within(screen.getByRole('region', { name: 'Pharmacy' }));
  expect(pharmacy.getByRole('button', { name: 'Pharmacy: Apotheke am Dom' })).toHaveTextContent('Employee');
  expect(pharmacy.getByRole('link', { name: 'Register a pharmacy' })).toBeInTheDocument();
});

test('the pharmacy switcher in the sidebar speaks German by default', async () => {
  localStorage.setItem('pharmacy', markt.id);
  answer([markt, dom]);

  await renderAt('/');

  const pharmacy = within(screen.getByRole('region', { name: 'Apotheke' }));
  expect(pharmacy.getByRole('button', { name: 'Apotheke: Apotheke am Markt' })).toHaveTextContent('Apothekenleiter');
  expect(pharmacy.getByRole('link', { name: 'Apotheke registrieren' })).toBeInTheDocument();
});

// A detail page below a module (its routes.ts's details): an address with a parameter, governed by its own audience,
// named by its title in the breadcrumbs and listed neither in the sidebar nor in the palette.
const version = '0198c1a0-0000-7000-8000-0000000000c1';

test("at a course version's address the breadcrumbs lead home, to the editorial page and name the version page", async () => {
  await renderAt(`/editorial/versions/${version}`);

  const breadcrumbs = within(screen.getByRole('navigation', { name: t('ui.breadcrumb.label') }));
  expect(breadcrumbs.getByRole('link', { name: 'Startseite' })).toHaveAttribute('href', '/');
  expect(breadcrumbs.getByRole('link', { name: 'Redaktion' })).toHaveAttribute('href', '/editorial');
  expect(breadcrumbs.getByText('Schulungsversion')).toHaveAttribute('aria-current', 'page');
  expect(breadcrumbs.getByText(courseVersion())).toHaveAttribute('aria-current', 'page');
  // The detail's own segments get no crumbs: three steps, Home, the module and the detail.
  expect(breadcrumbs.getAllByRole('listitem').map((item) => item.textContent.trim())).toEqual([
    home(),
    editorial(),
    courseVersion(),
  ]);
  expect(breadcrumbs.queryByText('versions')).not.toBeInTheDocument();
  expect(breadcrumbs.queryByText(version)).not.toBeInTheDocument();
});

test("a course version's page is there for an editor and listed neither in the sidebar nor in the palette", async () => {
  const user = userEvent.setup();
  answer([]);
  await renderAt(`/editorial/versions/${version}`, { roles: ['editor'] });

  expect(await screen.findByRole('heading', { name: courseVersion(), level: 1 })).toBeInTheDocument();
  expect(screen.queryByRole('heading', { name: t('shell.gate.notAvailable') })).not.toBeInTheDocument();
  expect(sidebarLinks()).toEqual([home(), editorial()]);
  const navigation = within(screen.getByRole('navigation', { name: t('shell.navigation.label') }));
  for (const link of navigation.getAllByRole('link')) {
    expect(link.getAttribute('href')).not.toMatch(/^\/editorial\/versions/);
  }

  await user.keyboard('{Control>}k{/Control}');
  const dialog = await screen.findByRole('dialog', { name: palette() });
  const options = within(dialog)
    .getAllByRole('option')
    .map((option) => option.textContent.trim());
  expect(options).toEqual(titles(['shell.home', 'modules.editorial.title', ...forEditors]));
  expect(options).not.toContain(courseVersion());
  expect(options).not.toContain(reportEntry());
});

test("a course version's page is refused to a signed-in user who is no editor", async () => {
  answer([markt]);
  await renderAt(`/editorial/versions/${version}`, { roles: ['editor'] });
  expect(await screen.findByRole('heading', { name: courseVersion(), level: 1 })).toBeInTheDocument();
  cleanup();

  await renderAt(`/editorial/versions/${version}`, { roles: [] });

  const main = within(await screen.findByRole('main'));
  expect(main.getByRole('heading', { name: 'Nicht verfügbar', level: 1 })).toBeInTheDocument();
  expect(main.getByText('Diese Seite steht Ihnen nicht zur Verfügung.')).toBeInTheDocument();
  expect(main.getByText(t('shell.gate.notForYou'))).toBeInTheDocument();
  expect(screen.queryByRole('heading', { name: courseVersion() })).not.toBeInTheDocument();
});

// A detail is governed by its own audience, not its module's: the tests' report entry is for editors, though reports
// are for employees.
test('a detail page for editors is refused to an employee, though its module is theirs, and shown to an editor', async () => {
  answer([dom]);
  await renderAt(`/reports/entries/${version}`, { roles: [] });

  const main = within(await screen.findByRole('main'));
  expect(main.getByRole('heading', { name: t('shell.gate.notAvailable'), level: 1 })).toBeInTheDocument();
  expect(main.getByText(t('shell.gate.notForYou'))).toBeInTheDocument();
  expect(screen.queryByRole('heading', { name: reportEntry() })).not.toBeInTheDocument();
  cleanup();

  await renderAt(`/reports/entries/${version}`, { roles: ['editor'] });

  expect(await screen.findByRole('heading', { name: reportEntry(), level: 1 })).toBeInTheDocument();
  const breadcrumbs = within(screen.getByRole('navigation', { name: t('ui.breadcrumb.label') }));
  expect(breadcrumbs.getByRole('link', { name: reportsTitle() })).toHaveAttribute('href', '/reports');
  expect(breadcrumbs.getByText(reportEntry())).toHaveAttribute('aria-current', 'page');
});

test("switched to English, the breadcrumbs name a course version's page in English", async () => {
  await renderAt(`/editorial/versions/${version}`);

  setLanguage('en-GB', { remember: false });

  const breadcrumbs = within(await screen.findByRole('navigation', { name: 'Breadcrumb' }));
  expect(await breadcrumbs.findByText('Course version')).toHaveAttribute('aria-current', 'page');
  expect(breadcrumbs.getByRole('link', { name: 'Editorial' })).toHaveAttribute('href', '/editorial');
});
