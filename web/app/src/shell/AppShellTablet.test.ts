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
import { SIDEBAR_MOBILE_BREAKPOINT } from '@/components/ui/sidebar/utils';
import { createAppRouter } from '@/router';

// The user menu has its own test; here it stands in, so this test checks the shell around it.
vi.mock('@/auth/UserMenu.vue', async () => {
  const { h } = await import('vue');
  return { default: { render: () => h('p', 'The user menu') } };
});

// Each module's page stands in as its title: a real module's page needs the host, which this test does not have.
vi.mock('@/modules', async (importOriginal) => {
  const { h } = await import('vue');
  const modules = await importOriginal<typeof import('@/modules')>();
  return {
    ...modules,
    moduleRoutes: modules.moduleRoutes.map((route) => ({
      ...route,
      component: { render: () => h('main', [h('h1', route.title)]) },
    })),
  };
});

// GET /organisation/pharmacies/mine as useMyPharmacies answers it: two pharmacies, the first one the user manages.
const pharmacies = await vi.hoisted(async () => {
  const { ref } = await import('vue');
  return {
    data: ref<PagedResultOfMyPharmacyDto | undefined>(undefined),
    isPending: ref(false),
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

/** jsdom has no matchMedia and no layout: the viewport is this width, which the sidebar's media query reads. */
function viewportOf(width: number) {
  vi.stubGlobal('matchMedia', (query: string) => {
    const maxWidth = /\(max-width:\s*(\d+)px\)/.exec(query);
    const minWidth = /\(min-width:\s*(\d+)px\)/.exec(query);
    const matches =
      (maxWidth === null || width <= Number(maxWidth[1])) && (minWidth === null || width >= Number(minWidth[1]));
    return {
      matches,
      media: query,
      onchange: null,
      addEventListener: () => undefined,
      removeEventListener: () => undefined,
      addListener: () => undefined,
      removeListener: () => undefined,
      dispatchEvent: () => false,
    };
  });
}

beforeEach(() => {
  pharmacies.data.value = { items: [markt, dom], totalCount: 2, page: 1, pageSize: 100 };
  localStorage.setItem('pharmacy', markt.id);
});
afterEach(() => {
  vi.unstubAllGlobals();
  localStorage.clear();
});

const user = {
  user: computed(
    () => ({ profile: { preferred_username: 'someone', realm_access: { roles: [] } } }) as unknown as User,
  ),
  isAuthenticated: computed(() => true),
} as unknown as Auth;
const signedIn: Plugin = { install: (app) => app.provide(authKey, user) };

async function renderShell() {
  const router = createAppRouter(createMemoryHistory());
  await router.push('/');
  render(App, { global: { plugins: [router, createPinia(), signedIn] } });
  return router;
}

// A tablet held upright is 768 px wide (docs/domain/README.md, "Geräte"): the desktop sidebar's breakpoint.
test('at a tablet width of 768 px the sidebar shows the pharmacy switcher and the navigation beside the page', async () => {
  expect(SIDEBAR_MOBILE_BREAKPOINT).toBe(768);
  viewportOf(768);

  await renderShell();

  expect(document.querySelector('[data-slot="sidebar"]')).not.toHaveAttribute('data-mobile');
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Pharmacy: Apotheke am Markt' })).toBeInTheDocument();
  const navigation = within(screen.getByRole('navigation', { name: 'Modules' }));
  expect(navigation.getByRole('link', { name: 'Organisation' })).toBeInTheDocument();
});

test('below 768 px the sidebar is a sheet the trigger opens, which closes once the user has gone to a page', async () => {
  const typing = userEvent.setup();
  viewportOf(767);
  const router = await renderShell();
  expect(screen.queryByRole('navigation', { name: 'Modules' })).not.toBeInTheDocument();

  await typing.click(within(screen.getByRole('banner')).getByRole('button', { name: 'Toggle Sidebar' }));
  const sheet = await screen.findByRole('dialog', { name: 'Sidebar' });
  expect(within(sheet).getByRole('button', { name: 'Pharmacy: Apotheke am Markt' })).toBeInTheDocument();
  await typing.click(
    within(within(sheet).getByRole('navigation', { name: 'Modules' })).getByRole('link', { name: 'Learning' }),
  );

  await vi.waitFor(() => expect(router.currentRoute.value.path).toBe('/learning'));
  await vi.waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  expect(await screen.findByRole('heading', { name: 'Learning', level: 1 })).toBeInTheDocument();
});
