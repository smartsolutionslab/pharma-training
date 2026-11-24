import { useQueryClient } from '@tanstack/vue-query';
import { computed, type MaybeRefOrGetter, toValue, watch } from 'vue';
import {
  type GetOrganisationPharmaciesIdMembersParams,
  getGetOrganisationPharmaciesMineQueryKey,
  useGetOrganisationPharmaciesIdInvitations,
  useGetOrganisationPharmaciesIdMembers,
} from '@/api/generated';
import { refusesMembership } from './teamErrors';

/** Both lists are read as one page of the largest size the host answers (PageSize.Maximum): a pharmacy has a few dozen. */
export const teamParams = { page: 1, pageSize: 100 } satisfies GetOrganisationPharmaciesIdMembersParams;

/**
 * A pharmacy's active members and its invitations (GET /organisation/pharmacies/{id}/members and …/invitations), read
 * only while `enabled`: the page asks for neither without the capability to manage members. A list refused because the
 * membership is gone or may no longer manage members reads the caller's memberships again, once per refusal, so the
 * page follows what the host says the caller now is.
 */
export function useTeam(pharmacyId: MaybeRefOrGetter<string | undefined>, enabled: MaybeRefOrGetter<boolean>) {
  const id = () => toValue(pharmacyId) ?? '';
  const query = { enabled: computed(() => toValue(enabled) && toValue(pharmacyId) !== undefined) };
  const members = useGetOrganisationPharmaciesIdMembers(id, teamParams, { query });
  const invitations = useGetOrganisationPharmaciesIdInvitations(id, teamParams, { query });
  const queryClient = useQueryClient();
  for (const list of [members, invitations]) {
    watch(list.error, (error) => {
      if (refusesMembership(error)) {
        void queryClient.invalidateQueries({ queryKey: getGetOrganisationPharmaciesMineQueryKey() });
      }
    });
  }
  return { members, invitations };
}
