import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { cleanup, render, screen, waitFor } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { createPinia, type Pinia } from 'pinia';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import { defineComponent, h } from 'vue';
import { createMemoryHistory } from 'vue-router';
import type { MyPharmacyDto } from '@/api/generated';
import { createAppRouter } from '@/router';
import { useCurrentPharmacy } from '@/shell/useCurrentPharmacy';
import { useSelectedPharmacyStore } from '@/stores/selectedPharmacy';
import RegisterPharmacyPage from './RegisterPharmacyPage.vue';

const notify = vi.hoisted(() => ({ success: vi.fn(), failure: vi.fn() }));
vi.mock('@/lib/notify', () => ({ notify }));

const markt: MyPharmacyDto = {
  id: '0198c1a0-0000-7000-8000-0000000000a1',
  name: 'Apotheke am Markt',
  membershipId: '0198c1a0-0000-7000-8000-0000000000b1',
  role: 'Employee',
  jobFunction: 'Pta',
  canManageMembers: false,
  canAssignCourses: false,
};
const founded: MyPharmacyDto = {
  id: '0198c1a0-0000-7000-8000-0000000000a9',
  name: 'Neue Apotheke',
  membershipId: '0198c1a0-0000-7000-8000-0000000000b9',
  role: 'Manager',
  jobFunction: 'Pharmacist',
  canManageMembers: true,
  canAssignCourses: true,
};

// The host, as POST /organisation/pharmacies and GET /organisation/pharmacies/mine reach it through the API client: the
// user works in one pharmacy, and once they have registered another, the next read of their memberships holds both
// (the founder's membership is saved with the pharmacy).
const host = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>();
vi.stubGlobal('fetch', host);

beforeEach(() => {
  localStorage.clear();
  let memberships = [markt];
  host.mockImplementation(async (_input, init) => {
    if (init?.method === 'POST') {
      memberships = [markt, founded];
      return Response.json(founded.id, {
        status: 201,
        headers: { Location: `/organisation/pharmacies/${founded.id}` },
      });
    }
    return Response.json({ items: memberships, totalCount: memberships.length, page: 1, pageSize: 100 });
  });
  notify.success.mockReset();
  notify.failure.mockReset();
});
afterEach(() => {
  host.mockReset();
  localStorage.clear();
});
// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(cleanup);

// Stands in for the app shell: it names the pharmacy the user works in, as the switcher does, beside the page.
const ShellStandIn = defineComponent({
  setup() {
    const { current } = useCurrentPharmacy();
    return () => h('div', [h('p', `Current pharmacy: ${current.value?.name ?? 'none'}`), h(RegisterPharmacyPage)]);
  },
});

async function renderInShell(pinia: Pinia = createPinia()) {
  const router = createAppRouter(createMemoryHistory());
  await router.push('/organisation/register');
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  render(ShellStandIn, { global: { plugins: [router, pinia, [VueQueryPlugin, { queryClient }]] } });
  return { router };
}

/** Every pharmacy the store held as chosen, from now on. */
function choicesOf(pinia: Pinia) {
  const store = useSelectedPharmacyStore(pinia);
  const choices: (string | null)[] = [];
  store.$subscribe(() => choices.push(store.pharmacyId), { flush: 'sync' });
  return choices;
}

/** Registers "Neue Apotheke" through the page, as the user does. */
async function registerNeueApotheke() {
  const user = userEvent.setup();
  expect(await screen.findByText('Current pharmacy: Apotheke am Markt')).toBeInTheDocument();
  await user.click(screen.getByRole('textbox', { name: 'Pharmacy name' }));
  await user.paste('Neue Apotheke');
  await user.selectOptions(screen.getByRole('combobox', { name: 'Your job function' }), 'Pharmacist');
  await user.click(screen.getByRole('button', { name: 'Register pharmacy' }));
}

// The memberships are read again before the new pharmacy is chosen: chosen among memberships that do not hold it yet,
// it would be forgotten at once, and with two pharmacies none would be current.
test('a registered pharmacy becomes the current one and stays, the user is told, and led to its team page', async () => {
  const user = userEvent.setup();
  const { router } = await renderInShell();
  expect(await screen.findByText('Current pharmacy: Apotheke am Markt')).toBeInTheDocument();

  await user.click(screen.getByRole('textbox', { name: 'Pharmacy name' }));
  await user.paste('  Neue Apotheke ');
  await user.selectOptions(screen.getByRole('combobox', { name: 'Your job function' }), 'Pharmacist');
  await user.click(screen.getByRole('button', { name: 'Register pharmacy' }));

  await waitFor(() => expect(router.currentRoute.value.path).toBe('/organisation'));
  expect(notify.success).toHaveBeenCalledExactlyOnceWith('The pharmacy Neue Apotheke was registered.');
  expect(notify.failure).not.toHaveBeenCalled();
  expect(await screen.findByText('Current pharmacy: Neue Apotheke')).toBeInTheDocument();
  expect(localStorage.getItem('pharmacy')).toBe(founded.id);
  // The memberships were read again after the registration, not only before it.
  const registration = host.mock.calls.findIndex(([, init]) => init?.method === 'POST');
  expect(host.mock.calls.slice(registration + 1).some(([, init]) => init?.method !== 'POST')).toBe(true);
});

// The pharmacy exists, but the page cannot open it: chosen among memberships that do not hold it, it would be
// forgotten at once. It is not chosen; the user is told where to find it and sent to the start page.
test.each([
  ['the memberships cannot be read again', () => Response.json({ title: 'Broken.' }, { status: 500 })],
  [
    'the memberships read again do not hold it',
    () => Response.json({ items: [markt], totalCount: 1, page: 1, pageSize: 100 }),
  ],
])(
  'a registered pharmacy that cannot be opened because %s is not chosen, and the user is told to choose it',
  async (_reason, readAfterRegistration) => {
    let registered = false;
    host.mockImplementation(async (_input, init) => {
      if (init?.method === 'POST') {
        registered = true;
        return Response.json(founded.id, { status: 201 });
      }
      return registered
        ? readAfterRegistration()
        : Response.json({ items: [markt], totalCount: 1, page: 1, pageSize: 100 });
    });
    const pinia = createPinia();
    const choices = choicesOf(pinia);
    const { router } = await renderInShell(pinia);

    await registerNeueApotheke();

    await waitFor(() => expect(router.currentRoute.value.path).toBe('/'));
    expect(notify.success).toHaveBeenCalledExactlyOnceWith(
      'The pharmacy Neue Apotheke was registered, but it could not be opened. Choose it in the pharmacy switcher.',
    );
    expect(notify.failure).not.toHaveBeenCalled();
    expect(choices).not.toContain(founded.id);
    expect(localStorage.getItem('pharmacy')).not.toBe(founded.id);
    expect(screen.getByText('Current pharmacy: Apotheke am Markt')).toBeInTheDocument();
  },
);
