import { keepPreviousData } from '@tanstack/vue-query';
import { computed, type MaybeRefOrGetter, toValue, unref } from 'vue';
import { useGetCertificationMeCertificates } from '@/api/generated';

/** The list's page size: the host's default. */
export const certificatesPageSize = 20;

/**
 * The signed-in user's certificates, newest first, a page at a time, and the page they are of: undefined until the
 * host answers. Asks again whenever the page changes; until the host answers, the page shown before stays (so the
 * pager does too, and keeps the focus), and isPending is true.
 */
export function useMyCertificates(page: MaybeRefOrGetter<number>) {
  const params = computed(() => ({ page: toValue(page), pageSize: certificatesPageSize }));
  const { data, isPending, isPlaceholderData, isFetching, error, refetch } = useGetCertificationMeCertificates(params, {
    query: { placeholderData: keepPreviousData },
  });
  return {
    certificates: computed(() => data.value?.items),
    page: data,
    // unref: a stand-in for the hook (the axe check's) may answer without isPlaceholderData.
    isPending: computed(() => isPending.value || unref(isPlaceholderData) === true),
    /** Whether the host is being asked, a page shown before included: until it answers, the page is not settled. */
    isFetching,
    error,
    /** Reads the page shown again. */
    refetch,
  };
}
