import { computed, type MaybeRefOrGetter, toValue, unref } from 'vue';
import { z } from 'zod';
import {
  getGetEditorialCourseVersionsIdReturnsQueryKey,
  useGetEditorialCourseVersionsIdReturns,
} from '@/api/generated';
import { startsWithKey } from '@/api/queryKeys';

/** The return history's page size: the host's default. */
export const returnsPageSize = 20;

/**
 * The returns of a course version, newest first, a page at a time, and the page they are of: undefined until the host
 * answers. Asks nothing for an id that is no UUID. Asks again whenever the id or the page changes; until the host
 * answers another page of the same version, the page shown before stays (so the pager does too, and keeps the focus),
 * and isPending is true. Another version shows nothing of the one before while it loads (#376): its pager would ask for
 * the new version's pages.
 */
export function useCourseVersionReturns(versionId: MaybeRefOrGetter<string>, page: MaybeRefOrGetter<number>) {
  const id = computed(() => toValue(versionId));
  const params = computed(() => ({ page: toValue(page), pageSize: returnsPageSize }));
  const enabled = computed(() => z.uuid().safeParse(id.value).success);
  const { data, isPending, isPlaceholderData, isError } = useGetEditorialCourseVersionsIdReturns(id, params, {
    query: {
      enabled,
      placeholderData: (previous, previousQuery) =>
        startsWithKey(previousQuery?.queryKey, getGetEditorialCourseVersionsIdReturnsQueryKey(id.value))
          ? previous
          : undefined,
    },
  });
  return {
    returns: computed(() => data.value?.items),
    page: data,
    // unref: a stand-in for the hook (the axe check's) may answer without isPlaceholderData.
    isPending: computed(() => isPending.value || unref(isPlaceholderData) === true),
    isError,
  };
}
