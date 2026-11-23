import { useQueryClient } from '@tanstack/vue-query';
import {
  getGetOrganisationPharmaciesMineQueryKey,
  getGetOrganisationPharmaciesMineQueryOptions,
} from '@/api/generated';
import { myPharmaciesParams } from '@/shell/useMyPharmacies';
import { useSelectedPharmacyStore } from '@/stores/selectedPharmacy';

/**
 * Opens a pharmacy the user has just joined (registered it, or accepted an invitation to it): the memberships are read
 * again and only then the pharmacy chosen, since chosen among memberships that do not hold it yet it would be forgotten
 * at once. `open(id)` answers whether it was opened: false when the memberships could not be read again or do not
 * hold it, and then nothing is chosen.
 */
export function useOpenPharmacy() {
  const queryClient = useQueryClient();
  const selected = useSelectedPharmacyStore();

  async function open(id: string): Promise<boolean> {
    // Marked stale without a refetch of its own: the read below is the one every observer of the memberships shares.
    await queryClient.invalidateQueries({ queryKey: getGetOrganisationPharmaciesMineQueryKey(), refetchType: 'none' });
    let held = false;
    try {
      const mine = await queryClient.fetchQuery(getGetOrganisationPharmaciesMineQueryOptions(myPharmaciesParams));
      held = mine.items.some((membership) => membership.id === id);
    } catch {
      // The memberships could not be read again: the pharmacy cannot be opened.
    }
    if (held) {
      selected.choose(id);
    }
    return held;
  }

  return { open };
}
