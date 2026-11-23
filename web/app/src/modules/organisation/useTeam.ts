import { computed, type MaybeRefOrGetter, toValue } from 'vue';
import {
  type GetOrganisationPharmaciesIdMembersParams,
  useGetOrganisationPharmaciesIdInvitations,
  useGetOrganisationPharmaciesIdMembers,
} from '@/api/generated';

/** Both lists are read as one page of the largest size the host answers (PageSize.Maximum): a pharmacy has a few dozen. */
export const teamParams = { page: 1, pageSize: 100 } satisfies GetOrganisationPharmaciesIdMembersParams;

/**
 * A pharmacy's active members and its invitations (GET /organisation/pharmacies/{id}/members and …/invitations), read
 * only while `enabled`: the page asks for neither without the capability to manage members.
 */
export function useTeam(pharmacyId: MaybeRefOrGetter<string | undefined>, enabled: MaybeRefOrGetter<boolean>) {
  const id = () => toValue(pharmacyId) ?? '';
  const query = { enabled: computed(() => toValue(enabled) && toValue(pharmacyId) !== undefined) };
  const members = useGetOrganisationPharmaciesIdMembers(id, teamParams, { query });
  const invitations = useGetOrganisationPharmaciesIdInvitations(id, teamParams, { query });
  return { members, invitations };
}
