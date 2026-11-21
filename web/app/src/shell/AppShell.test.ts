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

// Each module's page stands in as its title, and beside the app's modules is one of the tests' own: a real module's
// page needs the host, which these tests do not have. The audiences are the modules' own.
vi.mock('@/modules', async (importOriginal) => {
  const { h } = await import('vue');
  const modules = await importOriginal<typeof import('@/modules')>();
  const page = (title: string) => ({ render: () => h('main', [h('h1', title)]) });
  const reports = { path: 'reports', title: 'Reports', audience: 'employee' as const, component: page('Reports') };
  return {
    ...modules,
    moduleRoutes: [...modules.moduleRoutes.map((route) => ({ ...route, component: page(route.title) })), reports],
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

const audiences = [
  {
    who: 'an employee',
    roles: [],
    memberships: [dom],
    pages: ['Home', 'Assessment', 'Certification', 'Compliance', 'Learning', 'Reports'],
  },
  {
    who: 'a member who may manage the pharmacy',
    roles: [],
    memberships: [markt],
    pages: ['Home', 'Assessment', 'Catalog', 'Certification', 'Compliance', 'Learning', 'Organisation', 'Reports'],
  },
  { who: 'an editor of no pharmacy', roles: ['editor'], memberships: [], pages: ['Home', 'Editorial'] },
  {
    who: 'an editor who is an employee',
    roles: ['editor'],
    memberships: [dom],
    pages: ['Home', 'Assessment', 'Certification', 'Compliance', 'Editorial', 'Learning', 'Reports'],
  },
];

test.each(audiences)('the sidebar shows $who the pages for them', async ({ roles, memberships, pages }) => {
  answer(memberships);

  await renderAt('/', { roles });

  expect(sidebarLinks()).toEqual(pages);
});

test.each(audiences)('the command palette offers $who the pages for them', async ({ roles, memberships, pages }) => {
  const user = userEvent.setup();
  answer(memberships);
  await renderAt('/', { roles });

  await user.keyboard('{Control>}k{/Control}');
  const palette = await screen.findByRole('dialog', { name: 'Go to' });

  expect(
    within(palette)
      .getAllByRole('option')
      .map((option) => option.textContent.trim()),
  ).toEqual(pages);
});

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
  expect(screen.queryByRole('heading', { name: 'Organisation' })).not.toBeInTheDocument();
  expect(screen.queryByRole('heading', { name: 'Not available' })).not.toBeInTheDocument();

  answer([markt]);

  expect(await screen.findByRole('heading', { name: 'Organisation', level: 1 })).toBeInTheDocument();
  expect(pageArea()?.querySelector('[data-slot="skeleton"]')).toBeNull();
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
  ).toEqual(['Home', ...moduleRoutes.map((module) => module.title)]);

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
