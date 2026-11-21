import { useGetOrganisationPharmaciesMine } from '@/api/generated';

/** The largest page the host answers (PageSize.Maximum): a user belongs to a handful of pharmacies, all in one page. */
const PageSize = 100;

/** The user's active memberships, as GET /organisation/pharmacies/mine answers them. */
export function useMyPharmacies() {
  const { data, isPending, isError, refetch } = useGetOrganisationPharmaciesMine({ pageSize: PageSize });
  return { data, isPending, isError, refetch };
}
