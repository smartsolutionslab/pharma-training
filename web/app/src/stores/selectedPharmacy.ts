import { defineStore } from 'pinia';
import { ref } from 'vue';
import { z } from 'zod';

const StorageKey = 'pharmacy';

// Storage is untrusted input: anything but a pharmacy's id reads as no selection, and blocked storage as nothing stored.
function stored(): string | null {
  try {
    const parsed = z.uuid().safeParse(localStorage.getItem(StorageKey));
    return parsed.success ? parsed.data : null;
  } catch {
    return null;
  }
}

function store(id: string | null) {
  try {
    if (id === null) {
      localStorage.removeItem(StorageKey);
    } else {
      localStorage.setItem(StorageKey, id);
    }
  } catch {
    // Blocked storage: the choice holds for this visit only.
  }
}

/** The pharmacy the user chose to work in, by its id, kept for the next visit; the membership itself stays a query's. */
export const useSelectedPharmacyStore = defineStore('selectedPharmacy', () => {
  const pharmacyId = ref<string | null>(stored());

  function choose(id: string) {
    pharmacyId.value = id;
    store(id);
  }

  function forget() {
    pharmacyId.value = null;
    store(null);
  }

  return { pharmacyId, choose, forget };
});
