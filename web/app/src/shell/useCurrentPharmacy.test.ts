import { cleanup, render, waitFor } from '@testing-library/vue';
import { createPinia, type Pinia } from 'pinia';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import { defineComponent } from 'vue';
import type { MyPharmacyDto, PagedResultOfMyPharmacyDto } from '@/api/generated';
import { useSelectedPharmacyStore } from '@/stores/selectedPharmacy';
import { useCurrentPharmacy } from './useCurrentPharmacy';

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
const elsewhere = '0198c1a0-0000-7000-8000-0000000000a9';

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

function failing() {
  pharmacies.data.value = undefined;
  pharmacies.isPending.value = false;
  pharmacies.isError.value = true;
}

beforeEach(() => {
  localStorage.clear();
  loading();
});
afterEach(() => {
  cleanup();
  localStorage.clear();
});

/** useCurrentPharmacy as a component of a page loaded afresh uses it, with a new Pinia. */
function renderCurrent(pinia: Pinia = createPinia()) {
  let used: ReturnType<typeof useCurrentPharmacy> | undefined;
  const Current = defineComponent({
    setup() {
      used = useCurrentPharmacy();
      return () => null;
    },
  });
  const { unmount } = render(Current, { global: { plugins: [pinia] } });
  if (used === undefined) {
    throw new Error('The component did not set up.');
  }
  return { current: used, store: useSelectedPharmacyStore(pinia), unmount };
}

test('without memberships there is no current pharmacy', () => {
  answer([]);

  const { current } = renderCurrent();

  expect(current.status.value).toBe('ready');
  expect(current.memberships.value).toEqual([]);
  expect(current.current.value).toBeUndefined();
});

test('with one membership it is the current pharmacy, without storing a choice', async () => {
  answer([dom]);

  const { current, store } = renderCurrent();

  expect(current.status.value).toBe('ready');
  expect(current.current.value).toEqual(dom);
  // Had the derived membership been stored, a second membership would leave it current.
  answer([markt, dom]);
  await waitFor(() => expect(current.current.value).toBeUndefined());
  expect(store.pharmacyId).toBeNull();
  expect(localStorage.getItem('pharmacy')).toBeNull();
});

test('with several memberships the stored one is current, also after a reload', () => {
  localStorage.setItem('pharmacy', dom.id);
  answer([markt, dom]);
  const first = renderCurrent();
  expect(first.current.current.value).toEqual(dom);
  first.unmount();

  const { current } = renderCurrent();

  expect(current.memberships.value).toEqual([markt, dom]);
  expect(current.current.value).toEqual(dom);
});

test('with several memberships and none chosen there is no current pharmacy until the user picks one', () => {
  answer([markt, dom]);
  const { current, store } = renderCurrent();
  expect(current.current.value).toBeUndefined();

  current.choose(markt.id);

  expect(current.current.value).toEqual(markt);
  expect(store.pharmacyId).toBe(markt.id);
  expect(localStorage.getItem('pharmacy')).toBe(markt.id);
});

test('a choice can be changed to another membership', () => {
  localStorage.setItem('pharmacy', markt.id);
  answer([markt, dom]);
  const { current } = renderCurrent();

  current.choose(dom.id);

  expect(current.current.value).toEqual(dom);
});

test('a stored pharmacy the user is no member of is ignored and forgotten once the memberships are known', async () => {
  localStorage.setItem('pharmacy', elsewhere);
  answer([markt, dom]);

  const { current, store } = renderCurrent();

  expect(current.current.value).toBeUndefined();
  await waitFor(() => expect(store.pharmacyId).toBeNull());
  expect(localStorage.getItem('pharmacy')).toBeNull();
});

test('with one membership a stale stored pharmacy is forgotten too', async () => {
  localStorage.setItem('pharmacy', elsewhere);
  answer([dom]);

  const { current, store } = renderCurrent();

  expect(current.current.value).toEqual(dom);
  await waitFor(() => expect(store.pharmacyId).toBeNull());
});

test('while the memberships load there is no current pharmacy and the stored one is kept', async () => {
  localStorage.setItem('pharmacy', dom.id);

  const { current } = renderCurrent();

  expect(current.status.value).toBe('loading');
  expect(current.current.value).toBeUndefined();

  answer([markt, dom]);

  await waitFor(() => expect(current.status.value).toBe('ready'));
  expect(current.current.value).toEqual(dom);
});

test('when the memberships could not be loaded there is no current pharmacy and the stored one is kept', async () => {
  localStorage.setItem('pharmacy', dom.id);
  failing();

  const { current, store } = renderCurrent();

  expect(current.status.value).toBe('failed');
  expect(current.current.value).toBeUndefined();
  expect(current.memberships.value).toEqual([]);
  expect(store.pharmacyId).toBe(dom.id);

  // Tried again, the memberships come: the stored choice was kept through the failure.
  answer([markt, dom]);
  await waitFor(() => expect(current.current.value).toEqual(dom));
  expect(localStorage.getItem('pharmacy')).toBe(dom.id);
});
