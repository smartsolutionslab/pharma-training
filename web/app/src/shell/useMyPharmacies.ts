import { type GetOrganisationPharmaciesMineParams, useGetOrganisationPharmaciesMine } from '@/api/generated';

/**
 * How the memberships are read: the largest page the host answers (PageSize.Maximum), as a user belongs to a handful of
 * pharmacies, all in one page. Whoever reads them outside this hook uses the same, so both share one cached query.
 */
export const myPharmaciesParams: GetOrganisationPharmaciesMineParams = { pageSize: 100 };

/** The user's active memberships, as GET /organisation/pharmacies/mine answers them. */
export function useMyPharmacies() {
  const { data, isPending, isError, refetch } = useGetOrganisationPharmaciesMine(myPharmaciesParams);
  return { data, isPending, isError, refetch };
}
