import { keepPreviousData } from '@tanstack/vue-query';
import { computed, type MaybeRefOrGetter, toValue, unref } from 'vue';
import { z } from 'zod';
import { useGetEditorialCourseVersionsIdReturns } from '@/api/generated';

/** The return history's page size: the host's default. */
export const returnsPageSize = 20;

/**
 * The returns of a course version, newest first, a page at a time, and the page they are of: undefined until the host
 * answers. Asks nothing for an id that is no UUID. Asks again whenever the id or the page changes; until the host
 * answers, the page shown before stays (so the pager does too, and keeps the focus), and isPending is true.
 */
export function useCourseVersionReturns(versionId: MaybeRefOrGetter<string>, page: MaybeRefOrGetter<number>) {
  const id = computed(() => toValue(versionId));
  const params = computed(() => ({ page: toValue(page), pageSize: returnsPageSize }));
  const enabled = computed(() => z.uuid().safeParse(id.value).success);
  const { data, isPending, isPlaceholderData, isError } = useGetEditorialCourseVersionsIdReturns(id, params, {
    query: { enabled, placeholderData: keepPreviousData },
  });
  return {
    returns: computed(() => data.value?.items),
    page: data,
    // unref: a stand-in for the hook (the axe check's) may answer without isPlaceholderData.
    isPending: computed(() => isPending.value || unref(isPlaceholderData) === true),
    isError,
  };
}
