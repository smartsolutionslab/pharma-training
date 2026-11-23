import { cleanup, render } from '@testing-library/vue';
import type { User } from 'oidc-client-ts';
import { createPinia } from 'pinia';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import { computed, defineComponent, type Plugin } from 'vue';
import type { MyPharmacyDto, PagedResultOfMyPharmacyDto } from '@/api/generated';
import { type Auth, authKey } from '@/auth/useAuth';
import { moduleRoutes } from '@/modules';
import { useVisibleModules, useVisiblePages } from './useVisibleModules';

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
vi.mock('./useMyPharmacies', () => ({ useMyPharmacies: () => pharmacies }));

const employee: MyPharmacyDto = {
  id: '0198c1a0-0000-7000-8000-0000000000a2',
  name: 'Apotheke am Dom',
  membershipId: '0198c1a0-0000-7000-8000-0000000000b2',
  role: 'Employee',
  jobFunction: 'Pta',
  canManageMembers: false,
  canAssignCourses: false,
};
const manager: MyPharmacyDto = {
  id: '0198c1a0-0000-7000-8000-0000000000a1',
  name: 'Apotheke am Markt',
  membershipId: '0198c1a0-0000-7000-8000-0000000000b1',
  role: 'Manager',
  jobFunction: 'Pharmacist',
  canManageMembers: true,
  canAssignCourses: true,
};

function answer(items: MyPharmacyDto[]) {
  pharmacies.data.value = { items, totalCount: items.length, page: 1, pageSize: 100 };
  pharmacies.isPending.value = false;
  pharmacies.isError.value = false;
}

beforeEach(() => {
  localStorage.clear();
  pharmacies.data.value = undefined;
  pharmacies.isPending.value = true;
  pharmacies.isError.value = false;
});
afterEach(() => {
  cleanup();
  localStorage.clear();
});

/** A signed-in user whose ID token holds these realm roles. */
function signedInWith(roles: string[]): Plugin {
  const auth = {
    user: computed(() => ({ profile: { preferred_username: 'someone', realm_access: { roles } } }) as unknown as User),
  } as unknown as Auth;
  return { install: (app) => app.provide(authKey, auth) };
}

function visibleTo(roles: string[]) {
  let visible: ReturnType<typeof useVisibleModules> | undefined;
  const Visible = defineComponent({
    setup() {
      visible = useVisibleModules();
      return () => null;
    },
  });
  render(Visible, { global: { plugins: [createPinia(), signedInWith(roles)] } });
  if (visible === undefined) {
    throw new Error('The component did not set up.');
  }
  return visible;
}

const paths = (routes: { path: string }[]) => routes.map((route) => route.path);
const inOrder = (wanted: string[]) => paths(moduleRoutes).filter((path) => wanted.includes(path));

test('a member of a pharmacy sees the pages for employees', () => {
  answer([employee]);

  expect(paths(visibleTo([]).value)).toEqual(inOrder(['assessment', 'certification', 'learning']));
});

test('a member who may manage the pharmacy sees the pages for managers too', () => {
  answer([manager]);

  expect(paths(visibleTo([]).value)).toEqual(
    inOrder(['assessment', 'catalog', 'certification', 'compliance', 'learning', 'organisation']),
  );
});

// The realm role manager is Fabric's sample policy; the manager pages follow the membership's capabilities instead.
test('the realm role manager alone shows no page for managers', () => {
  answer([employee]);

  expect(paths(visibleTo(['manager']).value)).toEqual(inOrder(['assessment', 'certification', 'learning']));
});

test('an editor without a pharmacy sees the editorial pages only', () => {
  answer([]);

  expect(paths(visibleTo(['editor']).value)).toEqual(['editorial']);
});

test('an editor who is a member of a pharmacy sees both', () => {
  answer([employee]);

  expect(paths(visibleTo(['editor']).value)).toEqual(inOrder(['assessment', 'certification', 'editorial', 'learning']));
});

test('while the memberships load no pharmacy page is visible, the editorial ones are', () => {
  expect(paths(visibleTo([]).value)).toEqual([]);
  expect(paths(visibleTo(['editor']).value)).toEqual(['editorial']);
});

test('with several memberships and none chosen no pharmacy page is visible', () => {
  answer([manager, employee]);

  expect(paths(visibleTo([]).value)).toEqual([]);
});

test('the visible pages follow the chosen pharmacy', () => {
  localStorage.setItem('pharmacy', manager.id);
  answer([manager, employee]);

  expect(paths(visibleTo([]).value)).toContain('organisation');
});

function pagesVisibleTo(roles: string[]) {
  let visible: ReturnType<typeof useVisiblePages> | undefined;
  const Visible = defineComponent({
    setup() {
      visible = useVisiblePages();
      return () => null;
    },
  });
  render(Visible, { global: { plugins: [createPinia(), signedInWith(roles)] } });
  if (visible === undefined) {
    throw new Error('The component did not set up.');
  }
  return visible;
}

// A page is named by its key, which the sidebar, the breadcrumbs and the palette translate in the language shown.
test('the pages below the modules are named by their title keys', () => {
  answer([]);

  expect(pagesVisibleTo([]).value).toEqual([
    { to: '/organisation/register', titleKey: 'modules.organisation.pages.register.title' },
  ]);
});
