import { storeToRefs } from 'pinia';
import { computed, watch } from 'vue';
import type { MyPharmacyDto } from '@/api/generated';
import { useSelectedPharmacyStore } from '@/stores/selectedPharmacy';
import { useMyPharmacies } from './useMyPharmacies';

const none: MyPharmacyDto[] = [];

/**
 * The user's memberships and the one they work in: the only one, or the one they chose among several. A stored choice
 * the memberships no longer hold is forgotten once they are known; while they load, or fail to load at all, nothing is current.
 */
export function useCurrentPharmacy() {
  const pharmacies = useMyPharmacies();
  const selected = useSelectedPharmacyStore();
  const { pharmacyId } = storeToRefs(selected);

  const status = computed<'loading' | 'failed' | 'ready'>(() => {
    // A refetch that fails in the background keeps the data it had: only a load that brought nothing has failed.
    if (pharmacies.isError.value && pharmacies.data.value === undefined) {
      return 'failed';
    }
    return pharmacies.isPending.value || pharmacies.data.value === undefined ? 'loading' : 'ready';
  });

  const memberships = computed(() => (status.value === 'ready' ? (pharmacies.data.value?.items ?? none) : none));

  const chosen = computed(() => memberships.value.find((membership) => membership.id === pharmacyId.value));

  const current = computed<MyPharmacyDto | undefined>(() => {
    const [only, ...others] = memberships.value;
    return others.length === 0 ? only : chosen.value;
  });

  watch(
    [status, chosen, pharmacyId],
    () => {
      if (status.value === 'ready' && pharmacyId.value !== null && chosen.value === undefined) {
        selected.forget();
      }
    },
    { immediate: true },
  );

  /** Loads the memberships again, after they could not be loaded. */
  const retry = () => {
    void pharmacies.refetch();
  };

  return { status, memberships, current, choose: selected.choose, retry };
}
