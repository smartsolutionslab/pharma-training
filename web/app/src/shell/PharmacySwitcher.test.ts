import { cleanup, render, screen, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { createPinia } from 'pinia';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import { defineComponent, h } from 'vue';
import { createMemoryHistory } from 'vue-router';
import type { MyPharmacyDto, PagedResultOfMyPharmacyDto } from '@/api/generated';
import { SidebarProvider } from '@/components/ui/sidebar';
import { createAppRouter } from '@/router';
import PharmacySwitcher from './PharmacySwitcher.vue';

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

beforeEach(() => {
  localStorage.clear();
  pharmacies.data.value = undefined;
  pharmacies.isPending.value = true;
  pharmacies.isError.value = false;
  pharmacies.refetch.mockReset();
});
afterEach(() => {
  cleanup();
  localStorage.clear();
});

// The switcher sits in the sidebar's header, so it renders inside the sidebar's provider; each render is a page loaded
// afresh, with a new Pinia, on the start page.
const InSidebar = defineComponent({ render: () => h(SidebarProvider, null, () => h(PharmacySwitcher)) });
let router = createAppRouter(createMemoryHistory());
const renderSwitcher = () => {
  router = createAppRouter(createMemoryHistory());
  return render(InSidebar, { global: { plugins: [router, createPinia()] } });
};

test('with several pharmacies the switcher names the chosen one and its role', () => {
  localStorage.setItem('pharmacy', markt.id);
  answer([markt, dom]);

  renderSwitcher();

  const trigger = screen.getByRole('button', { name: 'Pharmacy: Apotheke am Markt' });
  expect(trigger).toHaveTextContent('Manager');
});

test('the user switches to another pharmacy, and it stays chosen after a reload', async () => {
  const user = userEvent.setup();
  localStorage.setItem('pharmacy', markt.id);
  answer([markt, dom]);
  const { unmount } = renderSwitcher();

  await user.click(screen.getByRole('button', { name: 'Pharmacy: Apotheke am Markt' }));
  const menu = await screen.findByRole('menu', { name: 'Pharmacies' });
  expect(within(menu).getByRole('menuitemradio', { name: /Apotheke am Markt/ })).toHaveAttribute(
    'aria-checked',
    'true',
  );
  expect(within(menu).getByRole('menuitemradio', { name: /Apotheke am Dom/ })).toHaveAttribute('aria-checked', 'false');
  await user.click(within(menu).getByRole('menuitemradio', { name: /Apotheke am Dom/ }));

  const switched = await screen.findByRole('button', { name: 'Pharmacy: Apotheke am Dom' });
  expect(switched).toHaveTextContent('Employee');
  await vi.waitFor(() => expect(screen.queryByRole('menu')).not.toBeInTheDocument());
  unmount();

  renderSwitcher();
  expect(screen.getByRole('button', { name: 'Pharmacy: Apotheke am Dom' })).toBeInTheDocument();
});

test('with several pharmacies and none chosen the switcher asks for one', async () => {
  const user = userEvent.setup();
  answer([markt, dom]);
  renderSwitcher();

  await user.click(screen.getByRole('button', { name: 'Choose a pharmacy' }));
  const menu = await screen.findByRole('menu', { name: 'Pharmacies' });
  await user.click(within(menu).getByRole('menuitemradio', { name: /Apotheke am Markt/ }));

  expect(await screen.findByRole('button', { name: 'Pharmacy: Apotheke am Markt' })).toBeInTheDocument();
});

test('with one pharmacy the switcher names it and its role, with nothing to switch', () => {
  answer([dom]);

  renderSwitcher();

  expect(screen.getByText('Apotheke am Dom')).toBeInTheDocument();
  expect(screen.getByText('Employee')).toBeInTheDocument();
  expect(screen.queryByRole('button')).not.toBeInTheDocument();
});

test('without a pharmacy the switcher says so', () => {
  answer([]);

  renderSwitcher();

  expect(screen.getByText('You are not a member of any pharmacy yet.')).toBeInTheDocument();
  expect(screen.queryByRole('button')).not.toBeInTheDocument();
});

test('while the pharmacies load the switcher is a skeleton', () => {
  const { container } = renderSwitcher();

  expect(container.querySelector('[data-slot="skeleton"]')).not.toBeNull();
  expect(screen.queryByRole('button')).not.toBeInTheDocument();
  expect(screen.queryByRole('alert')).not.toBeInTheDocument();
});

test('when the pharmacies could not be loaded the switcher says so and tries again', async () => {
  const user = userEvent.setup();
  pharmacies.isPending.value = false;
  pharmacies.isError.value = true;
  renderSwitcher();

  expect(screen.getByRole('alert')).toHaveTextContent('Pharmacies could not be loaded.');
  await user.click(screen.getByRole('button', { name: 'Try again' }));

  expect(pharmacies.refetch).toHaveBeenCalledOnce();
});

const pharmacyRegion = () => within(screen.getByRole('region', { name: 'Pharmacy' }));

// Whatever the user's memberships, a new pharmacy is one link away, beside them and not inside the menu.
test.each([
  ['no pharmacy', []],
  ['one pharmacy', [dom]],
  ['several pharmacies', [markt, dom]],
])('with %s the switcher leads to the page that registers one', async (_, memberships: MyPharmacyDto[]) => {
  const user = userEvent.setup();
  answer(memberships);
  renderSwitcher();

  const link = pharmacyRegion().getByRole('link', { name: 'Register a pharmacy' });
  expect(link).toHaveAttribute('href', '/organisation/register');
  await user.click(link);

  await vi.waitFor(() => expect(router.currentRoute.value.path).toBe('/organisation/register'));
});

// Not while it is a skeleton, nor beside the alert that the pharmacies could not be loaded: only once they are known.
test.each([
  ['load', () => undefined],
  [
    'could not be loaded',
    () => {
      pharmacies.isPending.value = false;
      pharmacies.isError.value = true;
    },
  ],
])('while the pharmacies %s the switcher has no link to register one, until they are known', async (_, before) => {
  before();
  renderSwitcher();
  expect(pharmacyRegion().queryByRole('link', { name: 'Register a pharmacy' })).not.toBeInTheDocument();

  answer([]);

  expect(await pharmacyRegion().findByRole('link', { name: 'Register a pharmacy' })).toBeInTheDocument();
});
