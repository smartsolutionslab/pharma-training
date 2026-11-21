import { render, screen, within } from '@testing-library/vue';
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

// The user menu has its own test; here it stands in, so this test checks the shell around it.
vi.mock('@/auth/UserMenu.vue', async () => {
  const { h } = await import('vue');
  return { default: { render: () => h('p', 'The user menu') } };
});

// Each module's page, and each page below it, stands in as its title, and beside the app's modules is one of the tests'
// own, with a page below it for every signed-in user and one for editors: a real module's page needs the host, which
// these tests do not have. The audiences are the modules' own.
vi.mock('@/modules', async (importOriginal) => {
  const { h } = await import('vue');
  const modules = await importOriginal<typeof import('@/modules')>();
  const page = (title: string) => ({ render: () => h('main', [h('h1', title)]) });
  const reports = {
    path: 'reports',
    title: 'Reports',
    audience: 'employee' as const,
    component: page('Reports'),
    pages: [
      { path: 'new', title: 'New report', audience: 'signed-in' as const, component: page('New report') },
      { path: 'review', title: 'Review reports', audience: 'editor' as const, component: page('Review reports') },
    ],
  };
  return {
    ...modules,
    moduleRoutes: [
      ...modules.moduleRoutes.map((route) => ({
        ...route,
        component: page(route.title),
        pages: route.pages?.map((below) => ({ ...below, component: page(below.title) })),
      })),
      reports,
    ],
  };
});

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
  within(screen.getByRole('navigation', { name: 'Modules' }))
    .getAllByRole('link')
    .map((link) => link.textContent.trim());

test('the sidebar lists the start page and every module the user may see, from their routes', async () => {
  await renderAt('/');

  const navigation = within(screen.getByRole('navigation', { name: 'Modules' }));
  expect(sidebarLinks()).toEqual(['Home', ...moduleRoutes.map((module) => module.title)]);
  expect(navigation.getByRole('link', { name: 'Home' })).toHaveAttribute('aria-current', 'page');
});

// The pages below the modules each user may see, in the command palette after the modules: registering a pharmacy and
// the tests' new report are for every signed-in user, the tests' review for editors, whatever their module's audience.
const forEveryone = ['Register a pharmacy', 'New report'];
const forEditors = [...forEveryone, 'Review reports'];

const audiences = [
  {
    who: 'an employee',
    roles: [],
    memberships: [dom],
    pages: ['Home', 'Assessment', 'Certification', 'Compliance', 'Learning', 'Reports'],
    below: forEveryone,
  },
  {
    who: 'a member who may manage the pharmacy',
    roles: [],
    memberships: [markt],
    pages: ['Home', 'Assessment', 'Catalog', 'Certification', 'Compliance', 'Learning', 'Organisation', 'Reports'],
    below: forEveryone,
  },
  {
    who: 'an editor of no pharmacy',
    roles: ['editor'],
    memberships: [],
    pages: ['Home', 'Editorial'],
    below: forEditors,
  },
  {
    who: 'an editor who is an employee',
    roles: ['editor'],
    memberships: [dom],
    pages: ['Home', 'Assessment', 'Certification', 'Compliance', 'Editorial', 'Learning', 'Reports'],
    below: forEditors,
  },
  { who: 'a user of no pharmacy and no role', roles: [], memberships: [], pages: ['Home'], below: forEveryone },
];

test.each(audiences)('the sidebar shows $who the pages for them', async ({ roles, memberships, pages }) => {
  answer(memberships);

  await renderAt('/', { roles });

  expect(sidebarLinks()).toEqual(pages);
});

// The sidebar lists the modules only: a page below a module is reached from the palette, the breadcrumbs or a link.
test.each(audiences)(
  'the command palette offers $who the pages for them, the pages below the modules after the modules',
  async ({ roles, memberships, pages, below }) => {
    const user = userEvent.setup();
    answer(memberships);
    await renderAt('/', { roles });

    await user.keyboard('{Control>}k{/Control}');
    const palette = await screen.findByRole('dialog', { name: 'Go to' });

    expect(
      within(palette)
        .getAllByRole('option')
        .map((option) => option.textContent.trim()),
    ).toEqual([...pages, ...below]);
  },
);

test('the start page is there for a user with no pharmacy and no role', async () => {
  answer([]);

  await renderAt('/', { roles: [] });

  expect(sidebarLinks()).toEqual(['Home']);
  expect(screen.getByRole('heading', { name: 'pharma-training', level: 1 })).toBeInTheDocument();
});

test("an employee who opens a manager's page is told it is not available, and led home", async () => {
  answer([dom]);

  await renderAt('/organisation', { roles: [] });

  const main = within(await screen.findByRole('main'));
  expect(main.getByRole('heading', { name: 'Not available', level: 1 })).toBeInTheDocument();
  expect(main.getByText('This page is not available to you in Apotheke am Dom.')).toBeInTheDocument();
  expect(main.getByRole('link', { name: 'Go to Home' })).toHaveAttribute('href', '/');
  expect(screen.queryByRole('heading', { name: 'Organisation' })).not.toBeInTheDocument();
});

test('with several pharmacies and none chosen a pharmacy page asks the user to choose one', async () => {
  answer([markt, dom]);

  await renderAt('/learning', { roles: [] });

  const main = within(await screen.findByRole('main'));
  expect(main.getByRole('heading', { name: 'Not available', level: 1 })).toBeInTheDocument();
  expect(main.getByText('Choose a pharmacy to see this page.')).toBeInTheDocument();
  expect(screen.queryByRole('heading', { name: 'Learning' })).not.toBeInTheDocument();
});

test('a user of no pharmacy who opens a pharmacy page is told so', async () => {
  answer([]);

  await renderAt('/learning', { roles: [] });

  const main = within(await screen.findByRole('main'));
  expect(main.getByRole('heading', { name: 'Not available', level: 1 })).toBeInTheDocument();
  expect(main.getByText('You are not a member of any pharmacy yet.')).toBeInTheDocument();
});

test('an editor of no pharmacy reaches the editorial page', async () => {
  answer([]);

  await renderAt('/editorial', { roles: ['editor'] });

  expect(await screen.findByRole('heading', { name: 'Editorial', level: 1 })).toBeInTheDocument();
  expect(screen.queryByRole('heading', { name: 'Not available' })).not.toBeInTheDocument();
  expect(sidebarLinks()).toEqual(['Home', 'Editorial']);
});

test("switching from a pharmacy the user manages to one they work in closes the manager's page", async () => {
  localStorage.setItem('pharmacy', markt.id);
  answer([markt, dom]);
  const user = userEvent.setup();
  await renderAt('/organisation', { roles: [] });
  expect(await screen.findByRole('heading', { name: 'Organisation', level: 1 })).toBeInTheDocument();

  await user.click(screen.getByRole('button', { name: 'Pharmacy: Apotheke am Markt' }));
  const menu = await screen.findByRole('menu', { name: 'Pharmacies' });
  await user.click(within(menu).getByRole('menuitemradio', { name: /Apotheke am Dom/ }));

  expect(await screen.findByRole('heading', { name: 'Not available', level: 1 })).toBeInTheDocument();
  expect(screen.getByText('This page is not available to you in Apotheke am Dom.')).toBeInTheDocument();
  expect(screen.queryByRole('heading', { name: 'Organisation', level: 1 })).not.toBeInTheDocument();
  expect(sidebarLinks()).not.toContain('Organisation');
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
  expect(within(busy).getByText('Loading…')).toHaveClass('sr-only');
  expect(screen.queryByRole('heading', { name: 'Organisation' })).not.toBeInTheDocument();
  expect(screen.queryByRole('heading', { name: 'Not available' })).not.toBeInTheDocument();

  answer([markt]);

  expect(await screen.findByRole('heading', { name: 'Organisation', level: 1 })).toBeInTheDocument();
  expect(pageArea()?.querySelector('[data-slot="skeleton"]')).toBeNull();
});

test('a signed-in user who is no editor is told the editorial page is not available to them', async () => {
  answer([markt]);

  await renderAt('/editorial', { roles: [] });

  const main = within(await screen.findByRole('main'));
  expect(main.getByRole('heading', { name: 'Not available', level: 1 })).toBeInTheDocument();
  expect(main.getByText('This page is not available to you.')).toBeInTheDocument();
  expect(main.getByRole('link', { name: 'Go to Home' })).toHaveAttribute('href', '/');
  expect(screen.queryByRole('heading', { name: 'Editorial' })).not.toBeInTheDocument();
});

test('when the memberships could not be loaded a pharmacy page says so and tries again on request', async () => {
  const user = userEvent.setup();
  pharmacies.data.value = undefined;
  pharmacies.isPending.value = false;
  pharmacies.isError.value = true;
  pharmacies.refetch.mockClear();

  await renderLoadingAt('/learning', []);

  const main = within(await screen.findByRole('main'));
  expect(main.getByRole('heading', { name: 'Not available', level: 1 })).toBeInTheDocument();
  expect(main.getByText('Your pharmacies could not be loaded.')).toBeInTheDocument();
  expect(screen.queryByRole('heading', { name: 'Learning' })).not.toBeInTheDocument();

  await user.click(main.getByRole('button', { name: 'Try again' }));

  expect(pharmacies.refetch).toHaveBeenCalledOnce();
});

// TanStack Query keeps the data it had when a refetch in the background fails; the page the user is on stays.
test('a failed refetch that keeps the memberships leaves the page the user is on', async () => {
  const user = userEvent.setup();
  answer([dom]);
  await renderAt('/learning', { roles: [] });
  expect(await screen.findByRole('heading', { name: 'Learning', level: 1 })).toBeInTheDocument();

  pharmacies.isError.value = true;
  // The shell renders again once the palette opens: by then a shell that blanked itself would have done so.
  await user.keyboard('{Control>}k{/Control}');
  const palette = await screen.findByRole('dialog', { name: 'Go to' });

  expect(within(palette).getByRole('option', { name: 'Learning' })).toBeInTheDocument();
  await user.keyboard('{Escape}');
  await vi.waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  expect(screen.getByRole('heading', { name: 'Learning', level: 1 })).toBeInTheDocument();
  expect(screen.queryByRole('heading', { name: 'Not available' })).not.toBeInTheDocument();
});

// A module the user may not see is an unknown segment of the address to the breadcrumbs: they do not name it.
test('the breadcrumbs show a module the user may not see as its segment of the address', async () => {
  answer([dom]);

  await renderAt('/organisation/members', { roles: [] });

  const breadcrumbs = within(screen.getByRole('navigation', { name: 'breadcrumb' }));
  expect(breadcrumbs.getByRole('link', { name: 'organisation' })).toHaveAttribute('href', '/organisation');
  expect(breadcrumbs.queryByText('Organisation')).not.toBeInTheDocument();
  expect(breadcrumbs.getByText('members')).toHaveAttribute('aria-current', 'page');
});

test('the sidebar collapses from its trigger and opens again', async () => {
  const user = userEvent.setup();
  await renderAt('/');
  const sidebar = () => document.querySelector('[data-slot="sidebar"]');
  expect(sidebar()).toHaveAttribute('data-state', 'expanded');

  await user.click(within(screen.getByRole('banner')).getByRole('button', { name: 'Toggle Sidebar' }));
  await vi.waitFor(() => expect(sidebar()).toHaveAttribute('data-state', 'collapsed'));

  await user.click(within(screen.getByRole('banner')).getByRole('button', { name: 'Toggle Sidebar' }));
  await vi.waitFor(() => expect(sidebar()).toHaveAttribute('data-state', 'expanded'));
});

test('on the start page the breadcrumbs name only it', async () => {
  await renderAt('/');

  const breadcrumbs = within(screen.getByRole('navigation', { name: 'breadcrumb' }));
  expect(breadcrumbs.getByText('Home')).toHaveAttribute('aria-current', 'page');
  expect(breadcrumbs.getByText('Home')).not.toHaveAttribute('href');
});

// The current page is text, not a disabled link: the sidebar's link to it stays the only link of that name.
test('the page the user is on is text in the breadcrumbs, not a second link to it', async () => {
  await renderAt('/reports');

  const breadcrumbs = within(screen.getByRole('navigation', { name: 'breadcrumb' }));
  expect(breadcrumbs.queryByRole('link', { name: 'Reports' })).not.toBeInTheDocument();
  expect(breadcrumbs.getByText('Reports')).toHaveAttribute('aria-current', 'page');
  expect(screen.getAllByRole('link', { name: 'Reports' })).toHaveLength(1);
});

test("the breadcrumbs lead from the start page to a module's page and below it", async () => {
  await renderAt('/reports/details');

  const breadcrumbs = within(screen.getByRole('navigation', { name: 'breadcrumb' }));
  expect(breadcrumbs.getByRole('link', { name: 'Home' })).toHaveAttribute('href', '/');
  expect(breadcrumbs.getByRole('link', { name: 'Reports' })).toHaveAttribute('href', '/reports');
  expect(breadcrumbs.getByText('details')).toHaveAttribute('aria-current', 'page');
});

test.each([
  ['Ctrl+K', '{Control>}k{/Control}'],
  ['Cmd+K', '{Meta>}k{/Meta}'],
])('%s opens the command palette and Escape closes it', async (_, keys) => {
  const user = userEvent.setup();
  await renderAt('/');

  await user.keyboard(keys);
  const palette = await screen.findByRole('dialog', { name: 'Go to' });
  await vi.waitFor(() => expect(within(palette).getByRole('textbox', { name: 'Go to' })).toHaveFocus());
  expect(
    within(palette)
      .getAllByRole('option')
      .map((option) => option.textContent.trim()),
  ).toEqual(['Home', ...moduleRoutes.map((module) => module.title), ...forEditors]);

  await user.keyboard('{Escape}');
  await vi.waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
});

test('the keyboard alone goes to a page from the command palette', async () => {
  const user = userEvent.setup();
  const { router } = await renderAt('/');

  await user.keyboard('{Control>}k{/Control}');
  await screen.findByRole('dialog', { name: 'Go to' });
  await user.keyboard('{ArrowDown}'.repeat(moduleRoutes.length));
  await vi.waitFor(() => expect(screen.getByRole('option', { name: 'Reports' })).toHaveAttribute('data-highlighted'));
  await user.keyboard('{Enter}');

  await vi.waitFor(() => expect(router.currentRoute.value.path).toBe('/reports'));
  await vi.waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  expect(await screen.findByRole('heading', { name: 'Reports' })).toBeInTheDocument();
});

test('typing narrows the command palette to the pages that match', async () => {
  const user = userEvent.setup();
  await renderAt('/');

  await user.keyboard('{Control>}k{/Control}');
  await screen.findByRole('dialog', { name: 'Go to' });
  await user.keyboard('no such page');

  await vi.waitFor(() => expect(screen.queryAllByRole('option')).toHaveLength(0));
  expect(screen.getByText('No page matches.')).toBeInTheDocument();
});

test('the command palette goes to a page below a module', async () => {
  const user = userEvent.setup();
  const { router } = await renderAt('/');

  await user.keyboard('{Control>}k{/Control}');
  const palette = await screen.findByRole('dialog', { name: 'Go to' });
  await user.click(within(palette).getByRole('option', { name: 'New report' }));

  await vi.waitFor(() => expect(router.currentRoute.value.path).toBe('/reports/new'));
  expect(await screen.findByRole('heading', { name: 'New report', level: 1 })).toBeInTheDocument();
});

// A page below a module is governed by its own audience, not its module's: registering a pharmacy needs no pharmacy.
test('a page for every signed-in user is there for a user of no pharmacy and no role', async () => {
  answer([]);

  await renderAt('/reports/new', { roles: [] });

  expect(await screen.findByRole('heading', { name: 'New report', level: 1 })).toBeInTheDocument();
  expect(screen.queryByRole('heading', { name: 'Not available' })).not.toBeInTheDocument();
  expect(sidebarLinks()).toEqual(['Home']);
});

test('the page to register a pharmacy is there for a user of no pharmacy and no role', async () => {
  answer([]);

  await renderAt('/organisation/register', { roles: [] });

  expect(await screen.findByRole('heading', { name: 'Register a pharmacy', level: 1 })).toBeInTheDocument();
  expect(screen.queryByRole('heading', { name: 'Not available' })).not.toBeInTheDocument();
});

// Neither a skeleton nor "Not available": the page is the user's whatever their memberships turn out to be.
test('while the memberships load a page for every signed-in user shows at once, without a skeleton', async () => {
  await renderLoadingAt('/organisation/register', []);

  expect(screen.getByRole('heading', { name: 'Register a pharmacy', level: 1 })).toBeInTheDocument();
  expect(document.querySelector('[data-slot="sidebar-inset"] [data-slot="skeleton"]')).toBeNull();
  expect(screen.getByRole('main')).not.toHaveAttribute('aria-busy');
  expect(screen.queryByRole('heading', { name: 'Not available' })).not.toBeInTheDocument();
});

test('a page below a module for editors is refused to a user who is no editor, though the module is theirs', async () => {
  answer([dom]);

  await renderAt('/reports/review', { roles: [] });

  const main = within(await screen.findByRole('main'));
  expect(main.getByRole('heading', { name: 'Not available', level: 1 })).toBeInTheDocument();
  expect(main.getByText('This page is not available to you.')).toBeInTheDocument();
  expect(screen.queryByRole('heading', { name: 'Review reports' })).not.toBeInTheDocument();
});

test('a page below a module for editors is there for an editor of no pharmacy', async () => {
  answer([]);

  await renderAt('/reports/review', { roles: ['editor'] });

  expect(await screen.findByRole('heading', { name: 'Review reports', level: 1 })).toBeInTheDocument();
  expect(screen.queryByRole('heading', { name: 'Not available' })).not.toBeInTheDocument();
});

test("the breadcrumbs name a page below a module by its title, after the module's", async () => {
  await renderAt('/organisation/register');

  const breadcrumbs = within(screen.getByRole('navigation', { name: 'breadcrumb' }));
  expect(breadcrumbs.getByRole('link', { name: 'Home' })).toHaveAttribute('href', '/');
  expect(breadcrumbs.getByRole('link', { name: 'Organisation' })).toHaveAttribute('href', '/organisation');
  expect(breadcrumbs.getByText('Register a pharmacy')).toHaveAttribute('aria-current', 'page');
});

test('the breadcrumbs name a page the user may see by its title, below a module they may not', async () => {
  answer([]);

  await renderAt('/organisation/register', { roles: [] });

  const breadcrumbs = within(screen.getByRole('navigation', { name: 'breadcrumb' }));
  expect(breadcrumbs.getByRole('link', { name: 'organisation' })).toHaveAttribute('href', '/organisation');
  expect(breadcrumbs.getByText('Register a pharmacy')).toHaveAttribute('aria-current', 'page');
});

// A page the user may not see is an unknown segment of the address to the breadcrumbs, as a module is.
test('the breadcrumbs name a page below a module by its title only where the user may see it', async () => {
  answer([dom]);
  const { router } = await renderAt('/reports/review', { roles: [] });

  const breadcrumbs = within(screen.getByRole('navigation', { name: 'breadcrumb' }));
  expect(breadcrumbs.getByRole('link', { name: 'Reports' })).toHaveAttribute('href', '/reports');
  expect(breadcrumbs.getByText('review')).toHaveAttribute('aria-current', 'page');
  expect(breadcrumbs.queryByText('Review reports')).not.toBeInTheDocument();

  await router.push('/reports/new');

  expect(await breadcrumbs.findByText('New report')).toHaveAttribute('aria-current', 'page');
});

// The way in for a user of no pharmacy: the switcher in the sidebar leads to the page that registers one.
test("a user of no pharmacy follows the switcher's link to the page that registers one", async () => {
  const user = userEvent.setup();
  answer([]);
  const { router } = await renderAt('/', { roles: [] });

  await user.click(
    within(screen.getByRole('region', { name: 'Pharmacy' })).getByRole('link', { name: 'Register a pharmacy' }),
  );

  await vi.waitFor(() => expect(router.currentRoute.value.path).toBe('/organisation/register'));
  expect(await screen.findByRole('heading', { name: 'Register a pharmacy', level: 1 })).toBeInTheDocument();
});
