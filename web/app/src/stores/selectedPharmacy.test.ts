import { createPinia, setActivePinia } from 'pinia';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import { useSelectedPharmacyStore } from './selectedPharmacy';

const markt = '0198c1a0-0000-7000-8000-0000000000a1';
const dom = '0198c1a0-0000-7000-8000-0000000000a2';

/** The store as a page loaded afresh finds it: a new Pinia, reading what the last visit stored. */
function reload() {
  setActivePinia(createPinia());
  return useSelectedPharmacyStore();
}

beforeEach(() => {
  localStorage.clear();
});

afterEach(() => {
  vi.restoreAllMocks();
  localStorage.clear();
});

test('with nothing stored no pharmacy is selected', () => {
  expect(reload().pharmacyId).toBeNull();
});

test('a chosen pharmacy is selected at once and still after a reload', () => {
  const store = reload();

  store.choose(markt);

  expect(store.pharmacyId).toBe(markt);
  expect(localStorage.getItem('pharmacy')).toBe(markt);
  expect(reload().pharmacyId).toBe(markt);
});

test('choosing another pharmacy replaces the first', () => {
  const store = reload();
  store.choose(markt);

  store.choose(dom);

  expect(store.pharmacyId).toBe(dom);
  expect(reload().pharmacyId).toBe(dom);
});

test('forget clears the selection, also for the next visit', () => {
  const store = reload();
  store.choose(markt);

  store.forget();

  expect(store.pharmacyId).toBeNull();
  expect(localStorage.getItem('pharmacy')).toBeNull();
  expect(reload().pharmacyId).toBeNull();
});

// Storage is untrusted input: anything but a pharmacy's id reads as no selection.
test.each(['', 'not-a-pharmacy', '0198c1a0-0000-7000-8000', `"${markt}"`, `${markt} `, '{"pharmacyId":1}'])(
  'a stored %j is no selection',
  (stored) => {
    localStorage.setItem('pharmacy', stored);

    expect(reload().pharmacyId).toBeNull();
  },
);

test('with storage blocked nothing is selected and a choice still holds for the visit', () => {
  const blocked = () => {
    throw new DOMException('Storage is blocked.', 'SecurityError');
  };
  vi.spyOn(Storage.prototype, 'getItem').mockImplementation(blocked);
  vi.spyOn(Storage.prototype, 'setItem').mockImplementation(blocked);
  vi.spyOn(Storage.prototype, 'removeItem').mockImplementation(blocked);
  const store = reload();
  expect(store.pharmacyId).toBeNull();

  store.choose(markt);
  expect(store.pharmacyId).toBe(markt);

  store.forget();
  expect(store.pharmacyId).toBeNull();
});
